using System;
using System.IO;
using System.Text.Json;

namespace Keymon
{
    // PersistenceService의 역할:
    //   - 앱 시작 시 userData.json을 읽어 AnalysisEngine과 MetricCollector에 이전 학습값을 복원합니다.
    //   - 앱 종료 시 현재 학습값을 userData.json에 저장합니다.
    //
    // 이전에는 LoadUserData/SaveUserData가 App.xaml.cs에 있었습니다.
    // 이제 파일 입출력 책임이 이 클래스에 완전히 격리됩니다.
    public class PersistenceService
    {
        // Path.Combine: 여러 경로 조각을 OS에 맞는 구분자로 이어붙입니다.
        // AppDomain.CurrentDomain.BaseDirectory: 실행 파일(.exe)이 있는 폴더 경로
        private static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userData.json");

        // AnalysisEngine과 MetricCollector에 저장된 학습값을 파일에서 복원합니다.
        // engine, collector를 매개변수로 받아 직접 값을 채워줍니다.
        public void Load(AnalysisEngine engine, MetricCollector collector)
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                // JsonSerializer.Deserialize: JSON 문자열을 지정한 타입의 객체로 변환합니다.
                // <UserData>는 제네릭(Generic) 문법으로, 어떤 타입으로 변환할지 지정합니다.
                var data = JsonSerializer.Deserialize<UserData>(File.ReadAllText(FilePath));
                if (data == null) return;

                // 총 누적 카운터 복원
                collector.TotalKeyCount = data.KeyCount;
                collector.TotalMouseCount = data.MouseCount;
                collector.TotalBackspaceCount = data.BackspaceCount;
                collector.TotalAccumulatedKeys = data.TotalAccumulatedKeys;

                // AnalysisEngine 개인 베이스라인 복원
                engine.PersonalEmaKpm = data.PersonalEmaKpm;
                engine.PersonalEmaEr = data.PersonalEmaEr;
                engine.PersonalVarKpm = data.PersonalVarKpm;
                engine.PersonalVarEr = data.PersonalVarEr;
                engine.PersonalEmaDt = data.PersonalEmaDt;
                engine.PersonalEmaFt = data.PersonalEmaFt;
                engine.PersonalVarDt = data.PersonalVarDt;
                engine.PersonalVarFt = data.PersonalVarFt;
                engine.PersonalEmaMj = data.PersonalEmaMj;
                engine.PersonalVarMj = data.PersonalVarMj;

                // AnalysisEngine도 TotalAccumulatedKeys를 내부 계산에 사용합니다.
                engine.TotalAccumulatedKeys = data.TotalAccumulatedKeys;
            }
            catch { }
        }

        // 현재 학습값을 파일에 저장합니다.
        // 앱 종료 시 App.xaml.cs의 OnExit에서 호출됩니다.
        public void Save(AnalysisEngine engine, MetricCollector collector)
        {
            try
            {
                var data = new UserData
                {
                    KeyCount = collector.TotalKeyCount,
                    MouseCount = collector.TotalMouseCount,
                    BackspaceCount = collector.TotalBackspaceCount,
                    TotalAccumulatedKeys = collector.TotalAccumulatedKeys,
                    PersonalEmaKpm = engine.PersonalEmaKpm,
                    PersonalEmaEr = engine.PersonalEmaEr,
                    PersonalVarKpm = engine.PersonalVarKpm,
                    PersonalVarEr = engine.PersonalVarEr,
                    PersonalEmaDt = engine.PersonalEmaDt,
                    PersonalEmaFt = engine.PersonalEmaFt,
                    PersonalVarDt = engine.PersonalVarDt,
                    PersonalVarFt = engine.PersonalVarFt,
                    PersonalEmaMj = engine.PersonalEmaMj,
                    PersonalVarMj = engine.PersonalVarMj
                };

                // JsonSerializer.Serialize: 객체를 JSON 문자열로 변환합니다.
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
            }
            catch { }
        }

        // UserData: JSON 파일의 구조와 1:1로 대응하는 내부 데이터 클래스입니다.
        // private: 이 클래스 내부에서만 사용됩니다. 외부에 노출할 필요가 없습니다.
        // { get; set; }: JSON 역직렬화를 위해 읽기/쓰기 모두 가능해야 합니다.
        private class UserData
        {
            public int KeyCount { get; set; }
            public int MouseCount { get; set; }
            public int BackspaceCount { get; set; }
            public int TotalAccumulatedKeys { get; set; }
            public double PersonalEmaKpm { get; set; }
            public double PersonalEmaEr { get; set; }
            public double PersonalVarKpm { get; set; }
            public double PersonalVarEr { get; set; }
            public double PersonalEmaDt { get; set; }
            public double PersonalEmaFt { get; set; }
            public double PersonalVarDt { get; set; }
            public double PersonalVarFt { get; set; }
            public double PersonalEmaMj { get; set; }
            public double PersonalVarMj { get; set; }
        }
    }
}
