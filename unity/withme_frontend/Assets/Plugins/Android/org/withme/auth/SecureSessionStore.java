package org.withme.auth;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.GeneralSecurityException;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** API 23+. Only AES-GCM ciphertext goes to disk; keys stay in AndroidKeyStore. */
public final class SecureSessionStore {
    private static final String ALIAS = "withme.auth.session.v1";
    private static final String FILE = "withme.auth.encrypted";
    private static final String ENTRY = "session";
    private SecureSessionStore() { }

    private static KeyStore keys() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        return store;
    }
    private static SharedPreferences preferences(Context context) {
        return context.getSharedPreferences(FILE, Context.MODE_PRIVATE);
    }
    private static SecretKey createKey() throws Exception {
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS,
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
        return generator.generateKey();
    }
    public static synchronized boolean save(Context context, String plaintext) throws Exception {
        KeyStore store = keys();
        SecretKey key = store.containsAlias(ALIAS) ? (SecretKey)store.getKey(ALIAS, null) : createKey();
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key);
        byte[] encrypted = cipher.doFinal(plaintext.getBytes(StandardCharsets.UTF_8));
        byte[] iv = cipher.getIV();
        ByteBuffer packed = ByteBuffer.allocate(1 + iv.length + encrypted.length);
        packed.put((byte)iv.length).put(iv).put(encrypted);
        return preferences(context).edit().putString(ENTRY, Base64.encodeToString(packed.array(), Base64.NO_WRAP)).commit();
    }
    public static synchronized String read(Context context) throws Exception {
        String saved = preferences(context).getString(ENTRY, null);
        if (saved == null) return null;
        KeyStore store = keys();
        if (!store.containsAlias(ALIAS)) { clear(context); return null; }
        try {
            ByteBuffer packed = ByteBuffer.wrap(Base64.decode(saved, Base64.NO_WRAP));
            int size = packed.get() & 255;
            if (size != 12 || packed.remaining() < size + 16) throw new GeneralSecurityException();
            byte[] iv = new byte[size]; packed.get(iv);
            byte[] encrypted = new byte[packed.remaining()]; packed.get(encrypted);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, (SecretKey)store.getKey(ALIAS, null), new GCMParameterSpec(128, iv));
            return new String(cipher.doFinal(encrypted), StandardCharsets.UTF_8);
        } catch (GeneralSecurityException | IllegalArgumentException | java.nio.BufferUnderflowException ex) {
            clear(context);
            return null; // Restored backup/corrupt or invalidated key: reauthenticate.
        }
    }
    public static synchronized boolean clear(Context context) throws Exception {
        // Key deletion makes any stale ciphertext unusable even if preference commit fails.
        KeyStore store = keys();
        if (store.containsAlias(ALIAS)) store.deleteEntry(ALIAS);
        return preferences(context).edit().remove(ENTRY).commit();
    }
}
