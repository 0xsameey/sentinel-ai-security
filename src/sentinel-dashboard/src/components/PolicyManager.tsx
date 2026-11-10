import { useState, useEffect } from "react";
import {ConfirmationDialog} from "./ui";
import { ShieldCheck, UserRoundCheck, LockKeyhole } from "lucide-react";
import {
  updateSpendingPolicy,
  updateRecipient,
  updatePauseStatus,
} from "../services/policyTransactions";

type Props = {
  accountId: string | null;
  authenticated: boolean;
  paused: boolean;
  onUpdated: () => Promise<void>;
  onNotify: (type: "success" | "error" | "info", message: string) => void;
  currentMaximum?:string;
  currentDaily?:string;
};

function toYocto(value: string): string {
  if (!/^\d+(\.\d{1,24})?$/.test(value)) {
    throw new Error("Invalid NEAR amount.");
  }

  const [whole, fraction = ""] = value.split(".");

  return (
    BigInt(whole) * 10n ** 24n +
    BigInt(fraction.padEnd(24, "0"))
  ).toString();
}

export default function PolicyManager({
  accountId,
  authenticated,
  paused,
  onUpdated,
  onNotify,
  currentMaximum,
  currentDaily,
}: Props) {
  const [maxTx, setMaxTx] = useState("0.5");
  const [daily, setDaily] = useState("2");
  const [recipient, setRecipient] = useState("");
  const [busy, setBusy] = useState(false);
  const [dirty,setDirty]=useState(false);
  const [pending,setPending]=useState<null|(()=>Promise<{success:boolean;pendingConfirmation:boolean}>)>(null);
  useEffect(()=>{if(!dirty){if(currentMaximum)setMaxTx(currentMaximum);if(currentDaily)setDaily(currentDaily);}},[currentMaximum,currentDaily,dirty]);

  async function perform(
    action: () => Promise<{
      success: boolean;
      pendingConfirmation: boolean;
    }>,
  ) {
    if (!accountId || !authenticated) {
      onNotify("error", "Authenticate your owner wallet first.");
      return;
    }

    setBusy(true);

    try {
      const result = await action();

      if (result.pendingConfirmation) {
        onNotify("info", "Wallet confirmation is pending. Check your wallet.");
        return;
      }

      if (result.success) {
        onNotify("info", "Policy transaction submitted. Refresh to verify the current rules.");

        try {
          await onUpdated();
        } catch {
          onNotify(
            "info",
            "Policy transaction submitted; current rules could not be refreshed. Check the existing transaction before requesting another change.",
          );
        }
      }
    } catch (e) {
      onNotify("error", (e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const disabled = busy || !authenticated || !accountId;
  function execute(action:()=>Promise<{success:boolean;pendingConfirmation:boolean}>){setPending(()=>action);}

  return (
    <div className="policy-manager">
      <div className="policy-management-block">
        <h3>
          <ShieldCheck size={17} />
          Spending limits
        </h3>

        <div className="policy-form-grid">
          <label className="field">
            <span>Maximum per transaction (NEAR)</span>
            <input
              value={maxTx}
              onChange={(e) => {setDirty(true);setMaxTx(e.target.value);}}
              inputMode="decimal"
            />
          </label>

          <label className="field">
            <span>Daily limit (NEAR)</span>
            <input
              value={daily}
              onChange={(e) => {setDirty(true);setDaily(e.target.value);}}
              inputMode="decimal"
            />
          </label>
        </div>

        <button
          className="primary-button policy-action-btn"
          disabled={disabled}
          onClick={() =>
            execute(() =>
              updateSpendingPolicy(accountId!, toYocto(maxTx), toYocto(daily)),
            )
          }
        >
          Update spending policy
        </button>
      </div>

      <div className="policy-management-block">
        <h3>
          <UserRoundCheck size={17} />
          Approved recipients
        </h3>

        <label className="field">
          <span>NEAR recipient account</span>
          <input
            value={recipient}
            onChange={(e) => setRecipient(e.target.value)}
            placeholder="recipient.testnet"
          />
        </label>

        <div className="policy-button-row">
          <button
            className="primary-button policy-action-btn"
            disabled={disabled || !recipient.trim()}
            onClick={() =>
              execute(() => updateRecipient(accountId!, recipient, true))
            }
          >
            Approve recipient
          </button>

          <button
            className="outline-button policy-action-btn"
            disabled={disabled || !recipient.trim()}
            onClick={() =>
              execute(() => updateRecipient(accountId!, recipient, false))
            }
          >
            Revoke recipient
          </button>
        </div>
      </div>

      <div className="policy-management-block">
        <h3>
          <LockKeyhole size={17} />
          Emergency controls
        </h3>

        <p>Current state: {paused ? "Paused" : "Active"}</p>

        <button
          className="outline-button policy-action-btn"
          disabled={disabled}
          onClick={() =>
            execute(() => updatePauseStatus(accountId!, !paused))
          }
        >
          {paused ? "Unpause contract" : "Pause contract"}
        </button>
      </div>
      {!authenticated&&<p className="helper-text">Sign in with your owner wallet to change policies. Viewing this page does not request a signature.</p>}
      <ConfirmationDialog open={pending!==null} title="Review this policy change" busy={busy} onClose={()=>setPending(null)} onConfirm={()=>{if(pending)void perform(pending).finally(()=>setPending(null));}}><p>This action changes your contract's security policy. Your wallet will show the exact method and values before you sign. Cancel if they do not match your intended change.</p></ConfirmationDialog>
    </div>
  );
}
