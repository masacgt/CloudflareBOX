package com.cloudflarebox.app

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.SecureRandom
import java.security.Signature
import java.security.spec.MGF1ParameterSpec
import java.security.spec.X509EncodedKeySpec
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.OAEPParameterSpec
import javax.crypto.spec.PSource

object CryptoManager {
    private const val STORE = "AndroidKeyStore"
    private const val SIGN_ALIAS = "cloudflarebox_signing_v1"
    private const val LOCAL_ALIAS = "cloudflarebox_local_v1"

    private fun keyStore(): KeyStore = KeyStore.getInstance(STORE).apply { load(null) }

    private fun ensureSigningKey() {
        if (keyStore().containsAlias(SIGN_ALIAS)) return
        val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_RSA, STORE)
        generator.initialize(
            KeyGenParameterSpec.Builder(SIGN_ALIAS, KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY)
                .setKeySize(2048)
                .setDigests(KeyProperties.DIGEST_SHA256)
                .setSignaturePaddings(KeyProperties.SIGNATURE_PADDING_RSA_PKCS1)
                .build()
        )
        generator.generateKeyPair()
    }

    fun signingPublicSpkiB64(): String {
        ensureSigningKey()
        return Base64.encodeToString(keyStore().getCertificate(SIGN_ALIAS).publicKey.encoded, Base64.NO_WRAP)
    }

    fun sign(data: ByteArray): String {
        ensureSigningKey()
        val privateKey = keyStore().getKey(SIGN_ALIAS, null) as java.security.PrivateKey
        val signature = Signature.getInstance("SHA256withRSA")
        signature.initSign(privateKey)
        signature.update(data)
        return Base64.encodeToString(signature.sign(), Base64.NO_WRAP)
    }

    private fun ensureLocalKey() {
        if (keyStore().containsAlias(LOCAL_ALIAS)) return
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, STORE)
        generator.init(
            KeyGenParameterSpec.Builder(LOCAL_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setKeySize(256)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build()
        )
        generator.generateKey()
    }

    private fun localKey(): SecretKey {
        ensureLocalKey()
        return keyStore().getKey(LOCAL_ALIAS, null) as SecretKey
    }

    fun protectLocal(bytes: ByteArray): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, localKey())
        val encrypted = cipher.doFinal(bytes)
        return Base64.encodeToString(cipher.iv, Base64.NO_WRAP) + "." + Base64.encodeToString(encrypted, Base64.NO_WRAP)
    }

    fun unprotectLocal(value: String): ByteArray {
        val parts = value.split('.', limit = 2)
        require(parts.size == 2)
        val nonce = Base64.decode(parts[0], Base64.NO_WRAP)
        val encrypted = Base64.decode(parts[1], Base64.NO_WRAP)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, localKey(), GCMParameterSpec(128, nonce))
        return cipher.doFinal(encrypted)
    }

    fun newFileKey(): ByteArray = ByteArray(32).also { SecureRandom().nextBytes(it) }

    fun wrapForWindows(fileKey: ByteArray, windowsSpkiB64: String): String {
        val publicKey = KeyFactory.getInstance("RSA").generatePublic(X509EncodedKeySpec(Base64.decode(windowsSpkiB64, Base64.NO_WRAP)))
        val cipher = Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding")
        val spec = OAEPParameterSpec("SHA-256", "MGF1", MGF1ParameterSpec.SHA256, PSource.PSpecified.DEFAULT)
        cipher.init(Cipher.ENCRYPT_MODE, publicKey, spec)
        return Base64.encodeToString(cipher.doFinal(fileKey), Base64.NO_WRAP)
    }

    data class MetadataCipher(val nonceB64: String, val cipherB64: String)

    fun encryptMetadata(fileKey: ByteArray, json: ByteArray): MetadataCipher {
        val nonce = ByteArray(12).also { SecureRandom().nextBytes(it) }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, javax.crypto.spec.SecretKeySpec(fileKey, "AES"), GCMParameterSpec(128, nonce))
        val encrypted = cipher.doFinal(json)
        return MetadataCipher(Base64.encodeToString(nonce, Base64.NO_WRAP), Base64.encodeToString(encrypted, Base64.NO_WRAP))
    }
}
