## 기본 원칙
- `Excel 시트 1개 = 테이블 1개 = Schema.json 1개`
- 시트 이름에 `_Desc` 접미사가 붙은 시트는 설명용 시트로 간주하고 변환에서 제외
- 데이터 컬럼명이 Note인 컬럼은 설명용 컬럼으로 간주하고 변환에서 제외
 - 데이터 타입
  - 기본 타입: int, float, double, string, bool, byte, short
  - 열거형: enum (자동 생성 코드에서 enum 타입으로 변환)


## 전체 구조
```
SVN
 └─ Excel
      ├─ Item.xlsx
      │    ├─ ItemSheet
      │    └─ Item.schema.json
      ├─ Skill.xlsx
      │    ├─ SkillSheet
      │    └─ Skill.schema.json
Jenkins
 └─ Convert → Schema.json, code, Binary, Manifest.json
Cloud
 ├─ /data/{revision}/Manifest.json
 └─ /data/{revision}/Item.bytes
 ```

 ---

 ### 엑셀 시트 데이터 작성 규칙

1. 첫 3줄 고정
   - 1행: comment (주석)
   - 2행: 컬럼명
   - 3행: 데이터 타입 + usage

2. usage 값
   - Server / Client / Common 중 하나
   - 사용 위치를 지정

3. 타입
   - 기본: int, float, double, string, bool, byte, short
   - enum: 자동 변환

#### 예시
| comment | 시리얼   |        |        |
| ------- | -------- | ------ | ------ |
| usage   | SerialNo | Key    | Value  |
| Common  | int      | string | double |
|        | 1     |  Test      | 0   |

### 엑셀 열거형(enum) 작성 규칙

1. **블럭 시작**
   - 열거형 이름은 반드시 `Enum_` 접두사 사용
   - 해당 블럭이 시작됨을 나타냄

2. **블럭 구조**
   - 각 필드는 아래 순서로 작성:
     ```
     필드명 | 값 | 주석 | (빈 컬럼)
     ```
   - 빈 컬럼은 **열거형 블럭 구분용**으로 반드시 추가
   - 각 블럭 끝에는 **가장 필드가 많은 열거형 아래 한 줄 공백**을 넣어 다음 블럭과 구분

3. **여러 열거형 배치**
   - 한 시트에서 열거형 블럭을 **가로로 나란히 배치 가능**
   - 블럭 간 공백 컬럼 규칙은 동일하게 적용


#### 예시

| Enum_MoneyType |   |   |   | Enum_SoundType |   |   |   | Enum_MissionType |   |   |   |
|----------------|---|---|---|----------------|---|---|---|-----------------|---|---|---|
| None           | 0 |  |   | BGM       | 0 | BGM     |   | TotalPrice | 0 |   | 총 가격 |
| SC             | 1 | 실버 |   | BGM_Env  | 1 | BGM효과음 |   | Wanted     | 1 |   | 특정 물건 지정 |
| HC             | 2 | 골드 |   | SFX_2D   | 2 | 효과음 2D |   |            |   |   |          |
| IAP            | 3 | 인앱구매 |   | SFX_3D | 3 | 효과음 3D |   |            |   |   |          |
| AD             | 4 | 광고 |   | UI        | 4 | UI       |   |            |   |   |          |
| Free           | 5 | 무료 |   | Voice     | 5 | 보이스 챗 |   |            |   |   |          |
|                |   |      |   | MAX       | 6 |         |   |            |   |   |          |
| | | | | | | | | | | | |
| Enum_StealableType |   |   |   |
|-------------------|---|---|---|
| None              | 0 |   |   |
| TV                | 1 | 텔레비전 |   |

---


## 데이터 메모리 레이아웃
DOD(Data-Oriented Design) 관점에서 설계됨
 - 구조체 배열을 사용하여 테이블 당 힙 할당 최소화: N(레코드) + 1 -> 1 (구조체 배열)
 - 필드 크기에 따라 자동으로 선언 순서 정렬
 - 문자열은 공용 문자열 테이블을 만들고 인덱스로 접근 (구조체에서 참조 데이터 제거)

 
## 필드 ID 기반 바이너리
`[FieldId][Length][Value]` 형식으로 저장
- FieldId: 필드 식별자 (int, 4 bytes)
- Length: 데이터 길이 (int, 4 bytes)
- Value: raw bytes

** 순서 기반 바이너리는 구조 변경에 취약
** 모르는 필드 아이디 -> 스킵
** 누락 필드 -> 디폴트

---

## 역할
1. 중간 파일인 schema.json 파일 생성
	- 사람이 읽을 수 있는 형태
	- 오류 검증
	- 코드 생성에 사용
2. 바이너리 파일 생성
3. Manifest.json 파일 생성
	- 이 리비전이 무엇으로 만들어졌는지
	- 정확히 어떤 파일들이 포함되었는지
    - 데이터 해시
	- 변환 결과가 변조되지 않았는지를 한 번에 설명해주는 메타데이터 스냅샷


### Constant.Schema.json 예시
```
Constant.Schema.json
{
  "Table": "Item",
  "Target": "Common",
  "Version": 5,
  "Deprecated": false,
  "PrimaryKey": "SerialNo",
  "NextFieldId": 4,
  "Fields": {
    "SerialNo": {
      "id": 1,
      "type": "int",
      "Deprecated": false
    },
    "Key": {
      "id": 2,
      "type": "string"
    },
    "Value": {
      "id": 3,
      "type": "double"
    }
  }
}
```


### manifest.json 예시
```
Manifest.json
{
  "Revision": 12345,
  "generatedAt": "2025-12-15T02:10:00Z",
  "hash": {
    "Item.bytes": "sha256:...",
    "Skill.bytes": "sha256:..."
  }
}
```


## 자동 생성 코드 샘플
Data.Enum.cs // 자동 생성된 모든 열거형 포함
DataTable.cs // {Schema.Table}Data 클래스, {Schema.Table}DataTable 클래스 포함, {Schema.Table}DataTableLoader 클래스 포함
DataTableManager.cs // EncryptionKeyProvider, StringTable, TableManager 클래스 부분 포함
DataTableManager.Client.cs // 클라 전용 TableManager 부분 포함 (로드 코드)
DataTableManager.Server.cs // 서버 전용 TableManager 부분 포함 (로드 코드)

### Data.Enum.cs
```
    public enum Constant
    {
        Test = 1,
    }
```

### DataTable.cs
```
    /// <summary>
    /// Schema: Constant.json
    /// </summary>
    public readonly struct ConstantData
    {
        // --- 메모리 레이아웃을 고려해서 정렬된 필드 ---
        public readonly double Value;
        public readonly int SerialNo;
        public readonly int _keyIndex;

        // --- 문자열 속성 ---
        public string Key => DataTableManager.Instance.StringTable.GetString(_keyIndex);
        
        // 객체 유효성 검사
        public bool IsValid => SerialNo != default;

        public ConstantData(
            int serialno, 
            double value, 
            int keyIndex
        )
        {
            this.SerialNo = serialno;
            this.Value = value;
            this._keyIndex = keyIndex;
        }
    }

        /// <summary>
    /// Data table for Constant.
    /// </summary>
    public class ConstantDataTable
    {
        private readonly ConstantData[] _dataArray;
        private readonly Dictionary<int, int> _keyToIndexMap;
        private readonly static ConstantData _defaultInstance = new ConstantData();

        public ConstantDataTable(int capacity)
        {
            _dataArray = new ConstantData[capacity];
            _keyToIndexMap = new Dictionary<int, int>(capacity);
        }

        public ref readonly ConstantData Get(int serialNo)
        {
            if (_keyToIndexMap.TryGetValue(serialNo, out var index))
            {
                return ref _dataArray[index];
            }
        
            return ref _defaultInstance;
        }

        public ref readonly ConstantData FirstOrDefault(RefPredicate<ConstantData> predicate)
        {
            foreach (ref readonly var data in _dataArray.AsSpan())
            {
                if (predicate(data))
                {
                    return ref data;
                }
            }
            return ref _defaultInstance;
        }

    
        public ReadOnlySpan<ConstantData> GetAll() => _dataArray.AsSpan();

        public void Add(in ConstantData data)
        {
            var index = _keyToIndexMap.Count;
            _dataArray[index] = data;
            _keyToIndexMap.Add(data.SerialNo, index);
        }
    }

    /// <summary>
    /// Data loader for Constant.
    /// </summary>
    public static class ConstantDataTableLoader
    {
        private class Loader : DataTableLoaderBase<ConstantData, ConstantDataTable>
        {
            protected override ConstantDataTable CreateTable(int capacity)
                => new ConstantDataTable(capacity);

            protected override void AddToTable(ConstantDataTable table, in ConstantData data) 
                => table.Add(in data);

            protected override ConstantData LoadRecord(BinaryReader reader, int rowLength)
            {
                long recordEndPosition = reader.BaseStream.Position + rowLength;
                // 필드 변수 선언 및 초기화
                int serialNo = default;
                int keyIndex = default;
                double value = default;
                while (reader.BaseStream.Position < recordEndPosition)
                {
                    int fieldId = reader.ReadInt32();
                    int length = reader.ReadInt32();
                    switch (fieldId)
                    {
                        case 1: // SerialNo
                            serialNo = reader.ReadInt32();
                            break;
                        case 2: // Key
                            keyIndex = reader.ReadInt32();
                            break;
                        case 3: // Value
                            value = reader.ReadDouble();
                            break;
                        default:
                            // 알 수 없는 필드 ID인 경우, 해당 길이만큼 건너뛴다.
                            reader.BaseStream.Seek(length, SeekOrigin.Current);
                            break;
                    }
                }
                return new ConstantData(
                    serialno: serialNo, 
                    keyIndex: keyIndex, 
                    value: value
                );
            }
       }

       public static bool TryLoad(string filePath, byte[] key, [NotNullWhen(true)] out ConstantDataTable dataTable)
            => new Loader().TryLoad(filePath, key, out dataTable);
    }
```