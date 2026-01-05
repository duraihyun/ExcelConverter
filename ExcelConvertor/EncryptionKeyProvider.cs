using System;
using System.Text;
using System.IO;

namespace ExcelConvertor
{
    /// <summary>
    /// 환경별 암호화 키 제공자
    /// </summary>
    public interface IEncryptionKeyProvider
    {
        byte[] GetKey();
        string GetEnvironment();
    }

    /// <summary>
    /// 로컬 개발 환경용 - 고정된 개발 키 사용
    /// </summary>
    public class DevelopmentKeyProvider : IEncryptionKeyProvider
    {
        private static readonly byte[] DevKey = Convert.FromBase64String("ui6yt/8nILrWcg+xGVWliahscs6jJ6j6fDBo2gpZklY=");

        public byte[] GetKey() => DevKey;
        public string GetEnvironment() => "Development";
    }

    /// <summary>
    /// 프로덕션 환경용 - 외부에서 주입받은 키 사용
    /// </summary>
    public class ProductionKeyProvider : IEncryptionKeyProvider
    {
        private readonly byte[] _key;

        public ProductionKeyProvider(string base64Key)
        {
            _key = Convert.FromBase64String(base64Key);

            if (_key.Length != 32)
            {
                throw new ArgumentException("Encryption key must be 32 bytes (256 bits) long.");
            }
        }

        public byte[] GetKey() => _key;
        public string GetEnvironment() => "Production";
    }

    /// <summary>
    /// 환경 설정 파일에서 키를 로드
    /// </summary>
    public class ConfigurationKeyProvider : IEncryptionKeyProvider
    {
        private readonly byte[] _key;

        public ConfigurationKeyProvider(string configPath = "encryption.config")
        {
            if (File.Exists(configPath))
            {
                var config = File.ReadAllText(configPath);
                _key = Convert.FromBase64String(config.Trim());
            }
            else
            {
                // 파일이 없으면 개발 키 사용
                _key = Encoding.UTF8.GetBytes("DEV_KEY_DO_NOT_USE_IN_PRODUCTION_32B");
            }
        }

        public byte[] GetKey() => _key;
        public string GetEnvironment() => File.Exists("encryption.config") ? "Custom" : "Development";
    }
}
