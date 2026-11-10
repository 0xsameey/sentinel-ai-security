import { actionCreators } from "@near-wallet-selector/core";
import { getWalletSelector, CONTRACT_ID } from "./nearWallet";

export const MANUAL_TESTING = import.meta.env.VITE_MANUAL_TESTING === "true";

export type NearContractCall = {
  receiverId: string;
  methodName: string;
  args: Record<string, unknown>;
  gas: string;
  deposit: string;
};

export type TransactionResult = {
  hash: string | null;
  success: boolean;
  pendingConfirmation: boolean;
};

const OWNER_METHODS = new Set([
  "authorize_payment",
  "create_delegation",
  "revoke_delegation",
  "set_policy",
  "set_recipient_allowed",
  "set_paused",
  "cancel_payment",
]);

export async function signContractCall(
  call: NearContractCall,
  accountId: string,
): Promise<TransactionResult> {
  if (MANUAL_TESTING) throw new Error("Manual testing: blockchain transaction signing is disabled.");
  const selector = await getWalletSelector();

  if (!selector.isSignedIn()) {
    throw new Error("Connect your NEAR wallet first.");
  }

  const wallet = await selector.wallet();
  const accounts = await wallet.getAccounts();

  if (!accounts.some((x) => x.accountId === accountId)) {
    throw new Error("Connected wallet account has changed.");
  }

  if (call.receiverId !== CONTRACT_ID) {
    throw new Error("Unexpected destination contract.");
  }

  if (!OWNER_METHODS.has(call.methodName)) {
    throw new Error("Contract method is not permitted.");
  }

  if (BigInt(call.deposit) !== 0n) {
    throw new Error("Contract call must have zero deposit.");
  }

  const gas = BigInt(call.gas);

  if (gas <= 0n || gas > 300_000_000_000_000n) {
    throw new Error("Invalid transaction gas.");
  }

  const result = await wallet.signAndSendTransaction({
    signerId: accountId,
    receiverId: CONTRACT_ID,
    actions: [actionCreators.functionCall(call.methodName, call.args, gas, 0n)],
  });

  // Some wallets redirect rather than returning the outcome.
  if (!result) {
    return {
      hash: null,
      success: false,
      pendingConfirmation: true,
    };
  }

  const hash = result.transaction?.hash ?? null;

  const status = result.status;

  const success =
    typeof status === "object" &&
    status !== null &&
    ("SuccessValue" in status || "SuccessReceiptId" in status);

  if (!success) {
    throw new Error("Transaction was not successful. Check NEAR Explorer.");
  }

  return {
    hash,
    success: true,
    pendingConfirmation: false,
  };
}

export function makeContractCall(
  methodName: string,
  args: Record<string, unknown>,
  gas = "30000000000000",
): NearContractCall {
  return {
    receiverId: CONTRACT_ID,
    methodName,
    args,
    gas,
    deposit: "0",
  };
}
