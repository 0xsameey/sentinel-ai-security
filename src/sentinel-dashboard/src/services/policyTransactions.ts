import {
  makeContractCall,
  signContractCall,
  type TransactionResult,
} from "./nearTransactions";

export async function updateSpendingPolicy(
  accountId: string,
  maxPerTransactionYocto: string,
  dailyLimitYocto: string,
): Promise<TransactionResult> {
  const max = BigInt(maxPerTransactionYocto);
  const daily = BigInt(dailyLimitYocto);

  if (max <= 0n || daily <= 0n) {
    throw new Error("Spending limits must be positive.");
  }

  if (max > daily) {
    throw new Error("Transaction limit cannot exceed daily limit.");
  }

  const call = makeContractCall("set_policy", {
    max_per_transaction: max.toString(),
    daily_limit: daily.toString(),
  });

  return signContractCall(call, accountId);
}

export async function updateRecipient(
  accountId: string,
  recipient: string,
  allowed: boolean,
): Promise<TransactionResult> {
  const normalized = recipient.trim();

  if (!normalized || normalized.length > 64) {
    throw new Error("Enter a valid NEAR account ID.");
  }

  const call = makeContractCall("set_recipient_allowed", {
    recipient: normalized,
    allowed,
  });

  return signContractCall(call, accountId);
}

export async function updatePauseStatus(
  accountId: string,
  paused: boolean,
): Promise<TransactionResult> {
  return signContractCall(
    makeContractCall("set_paused", { paused }),
    accountId,
  );
}
