import { describe, expect, it } from "vitest";
import { b64ToBytes, bytesToHex, canonicalRequest, randomCode6, sha256Hex } from "./crypto";

describe("device request crypto", () => {
  it("computes the standard SHA-256 vector", async () => {
    expect(await sha256Hex("abc")).toBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
  });

  it("builds the canonical signed request exactly", () => {
    const canonical = canonicalRequest("post", "/api/v1/test?q=1", "deadbeef", "123", "nonce-1234567890");
    expect(new TextDecoder().decode(canonical)).toBe("POST\n/api/v1/test?q=1\ndeadbeef\n123\nnonce-1234567890");
  });

  it("decodes base64 into owned bytes", () => {
    const bytes = b64ToBytes("AAEC/w==");
    expect(bytesToHex(bytes)).toBe("000102ff");
    expect(bytes.buffer).toBeInstanceOf(ArrayBuffer);
  });

  it("creates a six digit pairing code", () => {
    expect(randomCode6()).toMatch(/^\d{6}$/);
  });
});
