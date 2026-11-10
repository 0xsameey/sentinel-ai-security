
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sentinel.Infrastructure.Near;

public sealed class NearTransactionVerifier
{
    private readonly HttpClient _client;
    private readonly NearOptions _options;

    public NearTransactionVerifier(
        HttpClient client,
        IOptions<NearOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<bool> VerifyAsync(
        string transactionHash,
        string signerAccountId,
        string paymentId,
        string operation,
        string expectedRecipient,
    string expectedAmountYocto,
    ulong expectedExpiresAtNs,
        CancellationToken ct = default)
    {
        if (operation is not ("Authorization" or "Execution"))
            throw new ArgumentException("Invalid operation.");

        var expectedMethod = operation == "Authorization"
            ? "authorize_payment"
            : "execute_payment";

        var rpcRequest = new
        {
            jsonrpc = "2.0",
            id = "sentinel-tx-verify",
            method = "tx",
            @params = new
            {
                tx_hash = transactionHash,
                sender_account_id = signerAccountId,
                wait_until = "FINAL"
            }
        };

        using var response = await _client.PostAsJsonAsync(
            "",
            rpcRequest,
            ct);

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);

        var root = document.RootElement;

        if (root.TryGetProperty("error", out _))
            throw new NearRpcException(
                "Transaction status unavailable.");

        if (!root.TryGetProperty("result", out var result))
            throw new NearRpcException(
                "Invalid transaction status response.");

        if (result.GetProperty("final_execution_status")
                .GetString() != "FINAL")
            throw new NearRpcException(
                "Transaction is not yet final.");

        var transaction = result.GetProperty("transaction");

        if (transaction.GetProperty("signer_id").GetString()
                != signerAccountId)
            return false;

        if (transaction.GetProperty("receiver_id").GetString()
                != _options.ContractId)
            return false;

        if (transaction.GetProperty("hash").GetString()
                != transactionHash)
            return false;

        var actions = transaction.GetProperty("actions");

        if (actions.GetArrayLength() != 1)
            return false;

        if (!actions[0].TryGetProperty(
                "FunctionCall", out var functionCall))
            return false;

        if (functionCall.GetProperty("method_name")
                .GetString() != expectedMethod)
            return false;

        if (functionCall.GetProperty("deposit").ToString() != "0")
            return false;

        var argsBase64 = functionCall
            .GetProperty("args").GetString()!;

        using var argsDocument = JsonDocument.Parse(
            Encoding.UTF8.GetString(
                Convert.FromBase64String(argsBase64)));

        var args = argsDocument.RootElement;

        if (args.GetProperty("id").GetString() != paymentId)
            return false;

        if (operation == "Authorization")
        {
            if (args.GetProperty("recipient").GetString()
                    != expectedRecipient)
                return false;

            if (args.GetProperty("amount_yocto").GetString()
                    != expectedAmountYocto)
                return false;

            if (args.GetProperty("expires_at_ns").GetString()
                    != expectedExpiresAtNs.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                return false;
        }
        else
        {
            // Execution is only allowed to specify the payment ID.
            if (args.EnumerateObject().Count() != 1)
                return false;
        }


        var finality = result.GetProperty(
            "final_execution_status").GetString();

        if (finality != "FINAL")
        {
            throw new NearRpcException(
                "Transaction finality has not been reached.");
        }

        var status = result.GetProperty("status");

        if (status.ValueKind != JsonValueKind.Object)
        {
            throw new NearRpcException(
                "Unexpected transaction status.");
        }

        if (status.TryGetProperty("Failure", out _))
        {
            throw new InvalidOperationException(
                "NEAR transaction execution failed.");
        }

        if (!status.TryGetProperty("SuccessValue", out _))
        {
            throw new NearRpcException(
                "Transaction outcome is not yet confirmed.");
        }


        // The payment state will also be independently
        // reconciled from the contract.
        return true;
    }
}
