import { useCallback, useEffect, useState } from "react";
import { Buffer } from "buffer";
import { getWalletSelector, openWalletModal } from "../services/nearWallet";
import { sentinelApi } from "../api";

type WalletSession = {
  accountId: string | null;
  token: string | null;
  error: string | null;
  loading: boolean;
};

export function useWalletAuth() {
  const [session, setSession] = useState<WalletSession>({
    accountId: null,
    token: null,
    error: null,
    loading: false,
  });

  const refreshAccount = useCallback(async () => {
    const selector = await getWalletSelector();

    if (!selector.isSignedIn()) {
      setSession({
        accountId: null,
        token: null,
        error: null,
        loading: false,
      });
      return;
    }

    const wallet = await selector.wallet();
    const accounts = await wallet.getAccounts();
    const accountId = accounts[0]?.accountId ?? null;

    setSession((previous) => ({
      ...previous,
      accountId,
      token: previous.accountId === accountId ? previous.token : null,
    }));
  }, []);

  useEffect(() => {
    void refreshAccount().catch(console.error);

    let subscription: { unsubscribe(): void } | undefined;

    let active = true;

    void getWalletSelector().then((selector) => {
      if (!active) return;

      subscription = selector.store.observable.subscribe(() => {
        void refreshAccount().catch(console.error);
      });
    });

    return () => {
      active = false;
      subscription?.unsubscribe();
    };
  }, [refreshAccount]);

  const connect = useCallback(async () => {
    try {
      await openWalletModal();
    } catch (error) {
      setSession((previous) => ({
        ...previous,
        error: (error as Error).message,
      }));
    }
  }, []);

  const authenticate = useCallback(async () => {
    setSession((previous) => ({
      ...previous,
      loading: true,
      error: null,
    }));

    try {
      const selector = await getWalletSelector();
      const wallet = await selector.wallet();

      const accounts = await wallet.getAccounts();
      const accountId = accounts[0]?.accountId;

      if (!accountId) {
        throw new Error("Connect your wallet first.");
      }

      const challenge = await sentinelApi.createChallenge(accountId);

      if (Date.parse(challenge.expiresAtUtc) <= Date.now()) {
        throw new Error("Authentication challenge expired.");
      }

      const nonce = Buffer.from(challenge.nonceBase64, "base64");

      if (nonce.length !== 32) {
        throw new Error("Invalid authentication nonce.");
      }

      const signed = await wallet.signMessage({
        message: challenge.message,
        recipient: challenge.recipient,
        nonce,
      });

      if (!signed) {
        throw new Error(
          "This wallet requires redirect-based signing. " +
            "Use the supported extension wallet for this step.",
        );
      }

      if (signed.accountId !== accountId) {
        throw new Error("Wallet account mismatch.");
      }

      const publicKey = signed.publicKey;

      const signatureBase64 =
        typeof signed.signature === "string"
          ? signed.signature
          : Buffer.from(signed.signature).toString("base64");

      const result = await sentinelApi.verifyWallet({
        challengeId: challenge.challengeId,
        accountId,
        publicKey,
        signatureBase64,
      });

      if (result.accountId !== accountId) {
        throw new Error("Backend account mismatch.");
      }

      setSession({
        accountId,
        token: result.accessToken,
        error: null,
        loading: false,
      });
    } catch (error) {
      setSession((previous) => ({
        ...previous,
        token: null,
        loading: false,
        error: (error as Error).message,
      }));
    }
  }, []);

  const disconnect = useCallback(async () => {
    if (import.meta.env.VITE_MANUAL_TESTING === "true") {
      setSession({accountId:null,token:null,error:null,loading:false});
      return; // Local logout only; wallet logout can request access-key removal.
    }
    const selector = await getWalletSelector();

    try {
      if (selector.isSignedIn()) {
        const wallet = await selector.wallet();
        await wallet.signOut();
      }
    } finally {
      setSession({
        accountId: null,
        token: null,
        error: null,
        loading: false,
      });
    }
  }, []);

  return {
    ...session,
    connect,
    authenticate,
    disconnect,
  };
}
