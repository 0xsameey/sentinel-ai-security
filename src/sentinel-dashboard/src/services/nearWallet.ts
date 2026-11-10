import { setupWalletSelector } from "@near-wallet-selector/core";
import { setupMeteorWallet } from "@near-wallet-selector/meteor-wallet";
import { setupModal } from "@near-wallet-selector/modal-ui";

import "@near-wallet-selector/modal-ui/styles.css";

export const CONTRACT_ID = import.meta.env.VITE_NEAR_CONTRACT_ID;

let selectorPromise: ReturnType<typeof setupWalletSelector> | null = null;

export function getWalletSelector() {
  if (!selectorPromise) {
    selectorPromise = setupWalletSelector({
      network: "testnet",
      modules: [setupMeteorWallet()],
    });
  }

  return selectorPromise;
}

export async function openWalletModal() {
  if (import.meta.env.VITE_MANUAL_TESTING === "true") {
    throw new Error("Manual testing permits existing wallet sessions only. This wallet SDK creates an access key on new connection; do not approve AddKey or a transaction.");
  }
  const selector = await getWalletSelector();

  const modal = setupModal(selector, {
    contractId: CONTRACT_ID,
    theme: "light",
  });

  modal.show();

  return selector;
}
