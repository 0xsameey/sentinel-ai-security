import {
  setupWalletSelector,
  actionCreators,
  type WalletSelector,
} from "@near-wallet-selector/core";

import { setupMyNearWallet } from "@near-wallet-selector/my-near-wallet";

type ContractCall = {
  receiverId: string;
  methodName: string;
  args: Record<string, unknown>;
  gas: string;
  deposit: string;
};

const CONTRACT_ID = "0xsameey-sentinel-v2.testnet";

let selectorPromise: Promise<WalletSelector> | undefined;

export function getSelector() {
  selectorPromise ??= setupWalletSelector({
    network: "testnet",
    modules: [setupMyNearWallet()],
  });

  return selectorPromise;
}

export async function submitPolicyTransaction(transaction: ContractCall) {
  const allowedMethods = new Set(["set_policy", "set_recipient_allowed"]);

  if (transaction.receiverId !== CONTRACT_ID) {
    throw new Error("Unexpected contract");
  }

  if (!allowedMethods.has(transaction.methodName)) {
    throw new Error("Unsupported policy operation");
  }

  if (transaction.deposit !== "0") {
    throw new Error("Policy calls must not transfer NEAR");
  }

  const selector = await getSelector();
  const wallet = await selector.wallet();

  const outcome = await wallet.signAndSendTransaction({
    receiverId: CONTRACT_ID,
    actions: [
      actionCreators.functionCall(
        transaction.methodName,
        transaction.args,
        BigInt(transaction.gas),
        0n,
      ),
    ],
  });

  return outcome;
}
