// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using System;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Math;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public class Cryptography : ICryptography
    {
        private CryptoAlgorithm _algorithm = CryptoAlgorithm.AES; // Default
        private byte[] _key;
        private byte[] _iv;
        private CryptoMode _mode = CryptoMode.GCM; // Default
        private CryptoPadding _padding = CryptoPadding.PKCS7; // Default
        private byte[] _salt;
        private int _iterationCount = 10000; // Default for PBKDF2
        private byte[] _inputData;
        private byte[] _signature; // Added for verification

        internal Cryptography()
        { }

        public IAllowCryptoConfig WithAlgorithm(CryptoAlgorithm alg)
        {
            _algorithm = alg;
            return this;
        }

        public IAllowCryptoConfig WithKey(byte[] key)
        {
            _key = key;
            return this;
        }

        public IAllowCryptoConfig WithIV(byte[] iv)
        {
            _iv = iv;
            return this;
        }

        public IAllowCryptoConfig WithMode(CryptoMode mode)
        {
            _mode = mode;
            return this;
        }

        public IAllowCryptoConfig WithPadding(CryptoPadding padding)
        {
            _padding = padding;
            return this;
        }

        /// <summary>
        /// Builds the AES cipher for .WithMode and .WithPadding: CBC or ECB, and PKCS7, ISO10126 or none.
        /// </summary>
        /// <returns>The cipher to encrypt or decrypt with</returns>
        private IBufferedCipher BuildAesCipher()
        {
            IBlockCipher engine = CryptoMode.CBC == _mode
                ? (IBlockCipher)new CbcBlockCipher(new AesEngine())
                : new AesEngine();   // ECB

            // NoPadding uses an unpadded cipher, which throws if the data is not whole blocks.
            IBufferedCipher retVal = CryptoPadding.NoPadding == _padding
                ? new BufferedBlockCipher(engine)
                : new PaddedBufferedBlockCipher(engine, CryptoPadding.ISO10126 == _padding
                    ? (IBlockCipherPadding)new ISO10126d2Padding()
                    : new Pkcs7Padding());

            return retVal;
        }

        public IAllowCryptoConfig WithSalt(byte[] salt)
        {
            _salt = salt;
            return this;
        }

        public IAllowCryptoConfig WithSignature(byte[] signature)
        {
            _signature = signature;
            return this;
        }
        public IAllowCryptoConfig WithIterationCount(int count)
        {
            if (count <= 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Iteration count [{count}] is invalid, using default [{_iterationCount}]", this, GPALObjectType.Cryptography);
                return this;
            }
            _iterationCount = count;
            return this;
        }

        public IAllowCryptoAction WithInputData(byte[] data)
        {
            _inputData = data;
            return this;
        }

        public IAllowCryptoConfig GenerateIV(out byte[] iv)
        {
            iv = null;
            if (_algorithm != CryptoAlgorithm.AES)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Algorithm [{_algorithm}] not supported for IV generation, defaulting to AES", this, GPALObjectType.Cryptography);
                _algorithm = CryptoAlgorithm.AES;
            }
            if (_mode == CryptoMode.ECB)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "IV generation not required for ECB mode", this, GPALObjectType.Cryptography);
                return this;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                $"Generating IV for algorithm [{_algorithm}], mode [{_mode}]",
                this, GPALObjectType.Cryptography);

            try
            {
                int ivSize = _mode == CryptoMode.GCM ? 12 : 16; // 12 bytes for GCM, 16 for CBC
                iv = new byte[ivSize];
                var random = new SecureRandom();
                random.NextBytes(iv);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "IV generation failed", this, GPALObjectType.Cryptography, ex);
                iv = null;
            }

            _iv = iv; // Store for state reuse
            return this;
        }

        public IAllowCryptoConfig Encrypt(out byte[] encryptedData)
        {
            encryptedData = null;
            if (_key == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot encrypt: missing key", this, GPALObjectType.Cryptography);
                return this;
            }
            if (_inputData == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot encrypt: missing input data", this, GPALObjectType.Cryptography);
                return this;
            }
            if (_mode != CryptoMode.ECB && _iv == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"IV is required for mode [{_mode}], defaulting to ECB", this, GPALObjectType.Cryptography);
                _mode = CryptoMode.ECB;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                $"Encrypting with algorithm [{_algorithm}], mode [{_mode}], padding [{_padding}]",
                this, GPALObjectType.Cryptography);

            try
            {
                encryptedData = PerformEncryption();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Encryption failed", this, GPALObjectType.Cryptography, ex);
                encryptedData = null;
            }
            return this;
        }

        public IAllowCryptoConfig Decrypt(out string decryptedValue)
        {
            decryptedValue = null;
            if (_key == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot decrypt: missing key", this, GPALObjectType.Cryptography);
                return this;
            }
            if (_inputData == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot decrypt: missing input data", this, GPALObjectType.Cryptography);
                return this;
            }
            if (_mode != CryptoMode.ECB && _iv == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"IV is required for mode [{_mode}], defaulting to ECB", this, GPALObjectType.Cryptography);
                _mode = CryptoMode.ECB;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                $"Decrypting with algorithm [{_algorithm}], mode [{_mode}], padding [{_padding}]",
                this, GPALObjectType.Cryptography);

            try
            {
                byte[] decryptedBytes = PerformDecryption();
                decryptedValue = System.Text.Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Decryption failed", this, GPALObjectType.Cryptography, ex);
                decryptedValue = null;
            }
            return this;
        }

        public IAllowCryptoConfig Sign(out byte[] signature)
        {
            signature = null;
            if (_key == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot sign: missing key", this, GPALObjectType.Cryptography);
                return this;
            }
            if (_inputData == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot sign: missing input data", this, GPALObjectType.Cryptography);
                return this;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                $"Signing with algorithm [{_algorithm}]",
                this, GPALObjectType.Cryptography);

            try
            {
                signature = PerformSigning();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Signing failed", this, GPALObjectType.Cryptography, ex);
                signature = null;
            }
            return this;
        }

        public IAllowCryptoConfig Verify(out bool isValid)
        {
            isValid = false;
            if (_key == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot verify: missing key", this, GPALObjectType.Cryptography);
                return this;
            }
            if (_inputData == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot verify: missing input data", this, GPALObjectType.Cryptography);
                return this;
            }
            if (_signature == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot verify: missing signature", this, GPALObjectType.Cryptography);
                return this;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                $"Verifying with algorithm [{_algorithm}]",
                this, GPALObjectType.Cryptography);

            try
            {
                isValid = PerformVerification();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Verification failed", this, GPALObjectType.Cryptography, ex);
                isValid = false;
            }
            return this;
        }

        public IAllowCryptoConfig Hash(out byte[] hash)
        {
            hash = null;
            if (_inputData == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot hash: missing input data", this, GPALObjectType.Cryptography);
                return this;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                $"Hashing with algorithm [{_algorithm}]" + (_salt != null ? $", salt length [{_salt.Length}]" : ""),
                this, GPALObjectType.Cryptography);

            try
            {
                hash = PerformHashing();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Hashing failed", this, GPALObjectType.Cryptography, ex);
                hash = null;
            }
            return this;
        }

        public IAllowCryptoConfig GenerateKey(out byte[] key)
        {
            key = null;
            if (_algorithm == CryptoAlgorithm.PBKDF2 && (_salt == null || _inputData == null))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot generate key with PBKDF2: missing salt or input data", this, GPALObjectType.Cryptography);
                return this;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                $"Generating key with algorithm [{_algorithm}]" + (_algorithm == CryptoAlgorithm.PBKDF2 ? $", iteration count [{_iterationCount}]" : ""),
                this, GPALObjectType.Cryptography);

            try
            {
                key = PerformKeyGeneration();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Key generation failed", this, GPALObjectType.Cryptography, ex);
                key = null;
            }
            _key = key;
            return this;
        }

        private byte[] PerformEncryption()
        {
            if (CryptoAlgorithm.AES != _algorithm && CryptoAlgorithm.RSA != _algorithm)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Algorithm [{_algorithm}] does not encrypt, using AES", this, GPALObjectType.Cryptography);
                _algorithm = CryptoAlgorithm.AES;
            }

            // RSA can only encrypt data smaller than the key, so it is for a small secret such as an AES key. Mode and
            // padding are ignored.
            if (CryptoAlgorithm.RSA == _algorithm)
                return RsaCipher(true);

            if (_mode == CryptoMode.GCM)
            {
                IAeadBlockCipher cipher = new GcmBlockCipher(new AesEngine());
                ICipherParameters parameters = new AeadParameters(new KeyParameter(_key), 128, _iv, null);
                cipher.Init(true, parameters);
                byte[] output = new byte[cipher.GetOutputSize(_inputData.Length)];
                int len = cipher.ProcessBytes(_inputData, 0, _inputData.Length, output, 0);
                cipher.DoFinal(output, len);
                return output;
            }
            else
            {
                IBufferedCipher cipher = BuildAesCipher();

                ICipherParameters parameters;
                if (_iv != null)
                {
                    parameters = new ParametersWithIV(new KeyParameter(_key), _iv);
                }
                else
                {
                    parameters = new KeyParameter(_key);
                }

                cipher.Init(true, parameters);
                return cipher.DoFinal(_inputData);
            }
        }

        private byte[] PerformDecryption()
        {
            if (CryptoAlgorithm.AES != _algorithm && CryptoAlgorithm.RSA != _algorithm)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Algorithm [{_algorithm}] does not decrypt, using AES", this, GPALObjectType.Cryptography);
                _algorithm = CryptoAlgorithm.AES;
            }

            // rsa decrypts with the private key
            if (CryptoAlgorithm.RSA == _algorithm)
                return RsaCipher(false);

            if (_mode == CryptoMode.GCM)
            {
                IAeadBlockCipher cipher = new GcmBlockCipher(new AesEngine());
                ICipherParameters parameters = new AeadParameters(new KeyParameter(_key), 128, _iv, null);
                cipher.Init(false, parameters);
                byte[] output = new byte[cipher.GetOutputSize(_inputData.Length)];
                int len = cipher.ProcessBytes(_inputData, 0, _inputData.Length, output, 0);
                cipher.DoFinal(output, len);
                return output;
            }
            else
            {
                IBufferedCipher cipher = BuildAesCipher();

                ICipherParameters parameters;
                if (_iv != null)
                {
                    parameters = new ParametersWithIV(new KeyParameter(_key), _iv);
                }
                else
                {
                    parameters = new KeyParameter(_key);
                }

                cipher.Init(false, parameters);
                return cipher.DoFinal(_inputData);
            }
        }

        private byte[] PerformSigning()
        {
            if (CryptoAlgorithm.SHA256withRSA != _algorithm && CryptoAlgorithm.ECDSA != _algorithm)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Algorithm [{_algorithm}] does not sign, using SHA256withRSA", this, GPALObjectType.Cryptography);
                _algorithm = CryptoAlgorithm.SHA256withRSA;
            }

            AsymmetricKeyParameter keyParam;
            try
            {
                // PrivateKeyFactory handles both RSA and EC keys.
                keyParam = PrivateKeyFactory.CreateKey(_key);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{_algorithm}] private key could not be read. It has to be the PKCS#8 encoding of a key for that algorithm", this, GPALObjectType.Cryptography, ex);
                return null;
            }

            ISigner signer = SignerUtilities.GetSigner(SignerName());
            signer.Init(true, keyParam);
            signer.BlockUpdate(_inputData, 0, _inputData.Length);
            return signer.GenerateSignature();
        }

        private bool PerformVerification()
        {
            if (CryptoAlgorithm.SHA256withRSA != _algorithm && CryptoAlgorithm.ECDSA != _algorithm)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Algorithm [{_algorithm}] does not verify, using SHA256withRSA", this, GPALObjectType.Cryptography);
                _algorithm = CryptoAlgorithm.SHA256withRSA;
            }

            AsymmetricKeyParameter keyParam;
            try
            {
                keyParam = PublicKeyFactory.CreateKey(_key);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{_algorithm}] public key could not be read. It has to be the X.509 SubjectPublicKeyInfo encoding of a key for that algorithm", this, GPALObjectType.Cryptography, ex);
                return false;
            }

            ISigner signer = SignerUtilities.GetSigner(SignerName());
            signer.Init(false, keyParam);
            signer.BlockUpdate(_inputData, 0, _inputData.Length);
            return signer.VerifySignature(_signature); // Assumes _inputData is the signature
        }

        private byte[] PerformHashing()
        {
            if (CryptoAlgorithm.SHA256 != _algorithm && CryptoAlgorithm.MD5 != _algorithm)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Algorithm [{_algorithm}] does not hash, using SHA256", this, GPALObjectType.Cryptography);
                _algorithm = CryptoAlgorithm.SHA256;
            }

            IDigest digest = DigestUtilities.GetDigest(CryptoAlgorithm.MD5 == _algorithm ? "MD5" : "SHA256");
            if (_salt != null)
            {
                digest.BlockUpdate(_salt, 0, _salt.Length);
            }
            digest.BlockUpdate(_inputData, 0, _inputData.Length);
            byte[] hash = new byte[digest.GetDigestSize()];
            digest.DoFinal(hash, 0);
            return hash;
        }

        private byte[] PerformKeyGeneration()
        {
            if (_algorithm == CryptoAlgorithm.PBKDF2)
            {
                if (_salt == null || _inputData == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot generate key with PBKDF2: missing salt or input data", this, GPALObjectType.Cryptography);
                    return null;
                }

                Pkcs5S2ParametersGenerator gen = new Pkcs5S2ParametersGenerator();
                gen.Init(_inputData, _salt, _iterationCount);
                return ((KeyParameter)gen.GenerateDerivedParameters("AES", 256)).GetKey();
            }

            if (_algorithm != CryptoAlgorithm.AES)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Algorithm [{_algorithm}] not supported, defaulting to AES", this, GPALObjectType.Cryptography);
                _algorithm = CryptoAlgorithm.AES;
            }

            var generator = GeneratorUtilities.GetKeyGenerator("AES");
            return generator.GenerateKey();
        }
        /// <summary>
        /// The BouncyCastle signer name for the algorithm the chain is set to.
        /// </summary>
        /// <returns>SHA256withECDSA for ECDSA, SHA256withRSA otherwise</returns>
        private string SignerName()
        {
            return CryptoAlgorithm.ECDSA == _algorithm ? "SHA256withECDSA" : "SHA256withRSA";
        }

        /// <summary>
        /// Encrypts with the public key or decrypts with the private key using RSA.<br/>
        /// For a small secret, typically a file's AES key; RSA cannot handle data larger than the key.
        /// </summary>
        /// <param name="encrypting">True to encrypt with a public key, false to decrypt with a private key</param>
        /// <returns>The result, or null when the key could not be read</returns>
        private byte[] RsaCipher(bool encrypting)
        {
            AsymmetricKeyParameter keyParam;

            try
            {
                keyParam = true == encrypting ? PublicKeyFactory.CreateKey(_key) : PrivateKeyFactory.CreateKey(_key);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"RSA {(true == encrypting ? "public" : "private")} key could not be read", this, GPALObjectType.Cryptography, ex);
                return null;
            }

            IBufferedCipher cipher = CipherUtilities.GetCipher("RSA/NONE/PKCS1Padding");
            cipher.Init(encrypting, keyParam);

            return cipher.DoFinal(_inputData);
        }

        #region Helpers
        /// <summary>
        /// Generates an ECDSA P-256 key pair for Sign and Verify.<br/>
        /// Same format as <see cref="GenerateRsaKeyPair"/>: PKCS#8 private key, X.509 public key.
        /// </summary>
        /// <returns>The private key and the public key, or nulls when generation failed</returns>
        public static (byte[], byte[]) GenerateEcdsaKeyPair()
        {
            try
            {
                var keyPairGen = GeneratorUtilities.GetKeyPairGenerator("ECDSA");
                keyPairGen.Init(new Org.BouncyCastle.Crypto.Parameters.ECKeyGenerationParameters(
                    Org.BouncyCastle.Asn1.X9.X962NamedCurves.GetOid("prime256v1"), new SecureRandom()));

                var keyPair = keyPairGen.GenerateKeyPair();

                byte[] privateKey = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).ToAsn1Object().GetDerEncoded();
                byte[] publicKey = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).ToAsn1Object().GetDerEncoded();

                return (privateKey, publicKey);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Could not generate an ECDSA key pair", null, GPALObjectType.Cryptography, ex);
                return (null, null);
            }
        }

        public static (byte[], byte[]) GenerateRsaKeyPair()
        {
            try
            {
                var keyPairGen = GeneratorUtilities.GetKeyPairGenerator("RSA");
                keyPairGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
                var keyPair = keyPairGen.GenerateKeyPair();

                // Private key (PKCS#8)
                var privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private);
                byte[] privateKey = privateKeyInfo.ToAsn1Object().GetDerEncoded();

                // Public key (X.509)
                var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public);
                byte[] publicKey = publicKeyInfo.ToAsn1Object().GetDerEncoded();

                return (privateKey, publicKey);
            }
            catch (Exception)
            {
                // Since not part of fluent interface, return default values instead of throwing
                return (null, null);
            }
        }
        #endregion Helpers
        public ICryptography ToGPALObject()
        {
            return this;
        }
    }
}
