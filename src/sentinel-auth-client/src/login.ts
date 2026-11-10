import "dotenv/config";
import { KeyPairSigner } from "near-api-js";
import type { KeyPairString } from "near-api-js";

type ChallengeResponse = {
  challengeId: string;
  message: string;
  recipient: string;
  nonceBase64: string;
  expiresAtUtc: string;
};

type AuthResponse = {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  accountId: string;
};

const apiUrl = process.env.API_BASE_URL;
if (!apiUrl) {
  throw new Error("API_BASE_URL is missing.");
}

console.log("Connecting to Sentinel API:", apiUrl);
const accountId = process.env.NEAR_ACCOUNT_ID;
const privateKey = process.env.NEAR_PRIVATE_KEY;

if (!apiUrl || !accountId || !privateKey) {
  throw new Error("Missing required environment variables");
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const url = new URL(path, apiUrl);

  console.log("Requesting:", url.toString());

  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
    redirect: "manual",
  });

  console.log("Response status:", response.status);

  if (response.status >= 300 && response.status < 400) {
    throw new Error(
      `Unexpected redirect to: ${response.headers.get("location")}`,
    );
  }

  if (!response.ok) {
    const error = await response.text();
    throw new Error(`${path} failed (${response.status}): ${error}`);
  }

  return (await response.json()) as T;
}

async function main() {
  // Step 1: Request a server-generated challenge.
  const challenge = await postJson<ChallengeResponse>(
    "/api/v1/auth/challenge",
    { accountId },
  );

  const nonce = Buffer.from(challenge.nonceBase64, "base64");

  if (nonce.length !== 32) {
    throw new Error("Invalid challenge nonce length");
  }

  if (new Date(challenge.expiresAtUtc) <= new Date()) {
    throw new Error("Challenge has expired");
  }
  if (!accountId) {
    throw new Error("NEAR account ID is required for signing.");
  }
  // Step 2: Sign using the official NEAR SDK.
  const signer = KeyPairSigner.fromSecretKey(privateKey as KeyPairString);
  console.log("Signing account:", accountId);
  console.log("Derived public key:", signer.getPublicKey().toString());

  const signed = await signer.signNep413Message(accountId, {
    message: challenge.message,
    recipient: challenge.recipient,
    nonce: new Uint8Array(nonce),
  });

  if (signed.accountId !== accountId) {
    throw new Error("Signer account mismatch");
  }

  // Step 3: Submit the actual signature for verification.
  const result = await postJson<AuthResponse>("/api/v1/auth/verify", {
    challengeId: challenge.challengeId,
    accountId: signed.accountId,
    publicKey: signed.publicKey.toString(),
    signatureBase64: Buffer.from(signed.signature).toString("base64"),
  });

  console.log("Authentication succeeded");
  console.log("Account:", result.accountId);
  console.log("Token type:", result.tokenType);
  console.log("Expires in:", result.expiresIn, "seconds");

  // Step 4: Access a JWT-protected endpoint.
  const meResponse = await fetch(`${apiUrl}/api/v1/auth/me`, {
    headers: {
      Authorization: `Bearer ${result.accessToken}`,
    },
  });

  if (!meResponse.ok) {
    throw new Error(`/auth/me failed: ${meResponse.status}`);
  }

  console.log("Authenticated account:", await meResponse.json());

  // Do not print the JWT or the secret key.
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
