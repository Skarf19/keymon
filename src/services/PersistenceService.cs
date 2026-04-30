using System;
using System.IO;
using System.Text.Json;

namespace Keymon
{
    // PersistenceService의 역할:
    //   - 앱 시작 시 userData.json을 읽어 AnalysisEngine과 MetricCollector에 이전 학습값을 복원합니다.
    //   - 앱 종료 시 현재 학습값을 userData.json에 저장합니다.
    public class PersistenceService
    {
        private static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userData.json");

        public void Load(AnalysisEngine engine, MetricCollector collector)
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                var data = JsonSerializer.Deserialize<UserData>(File.ReadAllText(FilePath));
                if (data == null) return;

                collector.TotalKeyCount = data.KeyCount;
                collector.TotalMouseCount = data.MouseCount;
                collector.TotalBackspaceCount = data.BackspaceCount;
                collector.TotalAccumulatedKeys = data.TotalAccumulatedKeys;

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
                engine.TotalAccumulatedKeys = data.TotalAccumulatedKeys;
            }
            catch { }
        }

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
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
            }
            catch { }
        }

        // private: 이 클래스 내부에서만 사용됩니다.
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
