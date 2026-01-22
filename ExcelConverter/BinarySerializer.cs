using NPOI.SS.UserModel;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ExcelConvertor
{
    /// <summary>
    /// 스키마 정보를 참조하여 엑셀 데이터를 바이너리 파일로 직렬화
    /// 각 필드는 [FieldId][Length][Value] 형식으로 저장
    /// </summary>
    internal class BinarySerializer
    {
        private readonly IEncryptionKeyProvider _keyProvider;

        BinarySerializer(IEncryptionKeyProvider provider)
        {
            _keyProvider = provider;
        }

        /// <summary>
        /// 스트링 테이블을 바이너리로 저장..
        /// </summary>
        /// <param name="stringTable"></param>
        /// <param name="outputFilePath"></param>
        /// <returns></returns>
        public bool SaveStringTable(StringTable stringTable, string outputFilePath, out string fileHash)
        {
            fileHash = string.Empty;
            try
            {
                using var memoryStream = new MemoryStream();
                using var writer = new BinaryWriter(memoryStream, Encoding.UTF8);

                // 자동 생성되는 코드에서 사용하는 타입을 맞춰야함
                var strings = stringTable.ToArray();

                // 문자열 수 쓰기
                writer.Write(strings.Length);

                // 각 문자열 쓰기
                foreach (var str in strings)
                {
                    var strBytes = Encoding.UTF8.GetBytes(str);
                    // 문자열 길이 쓰기
                    writer.Write(strBytes.Length);
                    // 문자열 데이터 쓰기
                    writer.Write(strBytes);
                }

                byte[] data = memoryStream.ToArray();
                byte[] compressData = CompressData(data);
                byte[] finalData = EncryptData(compressData);

                // SHA-256 해시 계산
                fileHash = ComputeHash(finalData);

                // 암호화가 완료된 데이터는 파일에 저장
                File.WriteAllBytes(outputFilePath, finalData);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving string table to {outputFilePath}: {ex.Message}");
                return false;
            }
        }


        public bool GenerateBinary(SchemaGenerationResult schema, StringTable stringTable, string outputFilePath, int headerRowCount, out string fileHash)
        {
            fileHash = string.Empty;
            var recordList = new List<byte[]>();

            // 데이터 행 순회 (헤더 제외)
            for (int i = headerRowCount; i <= schema.Sheet.LastRowNum; ++i)
            {
                IRow row = schema.Sheet.GetRow(i);
                if (row == null) 
                    continue;

                using var recordStream = new MemoryStream();
                using var recordWriter = new BinaryWriter(recordStream, Encoding.UTF8);

                // 스키마 필드 순서로 직렬화를 해야 자동 생성되는 로직과 일치한다. 성능은 떨어지지만 일관성이 중요하다
                foreach (var field in schema.CurrentSchema.Fields)
                {
                    if (field.Value.Deprecated)
                        continue;

                    var cell = row.GetCell(field.Value.ColumnIndex);
                    if (cell == null)
                    {
                        // 스키마에 정의된 필드가 셀에 존재하지 않는 경우는 허용 불가
                        Console.WriteLine($"Warning: Field not found in schema for column {field.Value.ColumnIndex} in row {i} at {schema.CurrentSchema.Table}.");
                        return false;
                    }

                    // [FieldId] 쓰기
                    recordWriter.Write(field.Value.Id);

                    WriteValue(recordWriter, cell, field.Value.Type, stringTable);
                }

                recordList.Add(recordStream.ToArray());
            }

            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream, Encoding.UTF8);

            // 헤더: 실제 처리된 레코드(행)의 수
            writer.Write(recordList.Count);

            foreach (var recordData in recordList)
            {
                // 레코드 크기 쓰기
                writer.Write(recordData.Length);
                // 레코드 데이터 쓰기
                writer.Write(recordData);
            }

            try
            {
                byte[] data = memoryStream.ToArray();
                byte[] compressData = CompressData(data);
                byte[] finalData = EncryptData(compressData);

                // SHA-256 해시 계산
                fileHash = ComputeHash(finalData);

                // 암호화가 완료된 데이터는 파일에 저장
                File.WriteAllBytes(outputFilePath, finalData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing binary file to {outputFilePath}: {ex.Message}");
                return false;
            }

            return true;
        }

        private static void WriteValue(BinaryWriter writer, ICell cell, string typeName, StringTable table)
        {
            byte[] valueBytes;

            if (cell.CellType != CellType.Blank)
            {
                switch (typeName.ToLower())
                {
                    case "int":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? (int)cell.NumericCellValue : int.Parse(cell.StringCellValue));
                        break;
                    case "uint":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? (uint)cell.NumericCellValue : uint.Parse(cell.StringCellValue));
                        break;
                    case "long":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? (long)cell.NumericCellValue : long.Parse(cell.StringCellValue));
                        break;
                    case "ulong":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? (ulong)cell.NumericCellValue : ulong.Parse(cell.StringCellValue));
                        break;
                    case "float":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? (float)cell.NumericCellValue : float.Parse(cell.StringCellValue));
                        break;
                    case "double":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? cell.NumericCellValue : double.Parse(cell.StringCellValue));
                        break;
                    case "string":
                        // 문자열은 스트링 테이블의 인덱스로 저장
                        int stringId = table.GetStringId(cell.StringCellValue);
                        valueBytes = BitConverter.GetBytes(stringId);
                        break;
                    case "bool":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Boolean ? cell.BooleanCellValue : bool.Parse(cell.StringCellValue));
                        break;
                    case "byte":
                        valueBytes = [cell.CellType == CellType.Numeric ? (byte)cell.NumericCellValue : byte.Parse(cell.StringCellValue)];
                        break;
                    case "short":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? (short)cell.NumericCellValue : short.Parse(cell.StringCellValue));
                        break;
                    case "ushort":
                        valueBytes = BitConverter.GetBytes(cell.CellType == CellType.Numeric ? (ushort)cell.NumericCellValue : ushort.Parse(cell.StringCellValue));
                        break;
                    default:
                        // 열거형인 경우
                        if (typeName.StartsWith("e.", StringComparison.OrdinalIgnoreCase))
                        {
                            // 문자열 그대로 저장
                            valueBytes = Encoding.UTF8.GetBytes(cell.StringCellValue);
                        }
                        else
                        {
                            // 죽여
                            throw new InvalidOperationException($"Unsupported data type: {typeName}");
                        }
                        break;
                }
            }
            else
            {
                valueBytes = [];
            }

            // [Length] 쓰기
            writer.Write(valueBytes.Length);

            // [Value] 쓰기
            writer.Write(valueBytes);
        }


        // 압축
        private static byte[] CompressData(byte[] source)
        {
            using var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true))
            {
                gzipStream.Write(source, 0, source.Length);
            }
            return compressedStream.ToArray();
        }

        /// <summary>
        /// 암호화
        /// </summary>
        /// <param name="plainData"></param>
        /// <returns></returns>
        private byte[] EncryptData(byte[] plainData)
        {
            using var aes = new AesGcm(_keyProvider.GetKey(), AesGcm.TagByteSizes.MaxSize);

            // Nonce는 각 암호화마다 고유해야 한다.
            var nonce = new byte[12]; // 12 bytes
            RandomNumberGenerator.Fill(nonce);

            var tag = new byte[16]; // 16 bytes
            var ciphertext = new byte[plainData.Length];
            // 추가 인증 데이터 (Associated Data)는 암호화되지는 않지만, 무결성은 보호된다
            // 여기서는 파일 매직 넘버와 버전을 사용
            var magic = new byte[] { 0x45, 0x58, 0x43, 0x42 }; // EXCB (엑셀 컨버트 바이너리)
            var version = new byte[] { 0x01 }; // Version 1
            var associatedData = new byte[magic.Length + version.Length];
            Buffer.BlockCopy(magic, 0, associatedData, 0, magic.Length);
            Buffer.BlockCopy(version, 0, associatedData, magic.Length, version.Length);

            aes.Encrypt(nonce, plainData, ciphertext, tag, associatedData);

            // 최종 파일 데이터 구성, 파일 포맷 정의
            //| Magic(4) | Ver(1) | Nonce(12) | Tag(16) | Ciphertext(N) |
            using var finalStream = new MemoryStream();
            finalStream.Write(associatedData, 0, associatedData.Length);
            finalStream.Write(nonce, 0, nonce.Length);
            finalStream.Write(tag, 0, tag.Length);
            finalStream.Write(ciphertext, 0, ciphertext.Length);

            return finalStream.ToArray();
        }


        private static string ComputeHash(byte[] data)
        {
            byte[] hashBytes = SHA256.HashData(data);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}