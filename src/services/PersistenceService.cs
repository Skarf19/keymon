using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keymon
{
    public class PersistenceService
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userData.json");

        public void Load(AnalysisEngine engine, MetricCollector collector, MonitoringService service)
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var data = JsonSerializer.Deserialize<UserData>(File.ReadAllText(FilePath), options);
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

                engine.FatigueScore = data.FatigueScore;
                engine.ContinuousWorkMinutes = data.ContinuousWorkMinutes;

                engine.FocusScore = data.FocusScore;
                engine.FocusState = data.FocusState;
                if (!string.IsNullOrEmpty(data.StateReason)) engine.StateReason = data.StateReason;

                engine.IsFirstAnalysisComplete = data.IsFirstAnalysisComplete || data.HistoryScores.Count > 0;

                service.RestoreHistory(data.HistoryScores, data.HistoryStates, data.HistoryFatigue);

                if (data.DailyStats != null)
                {
                    foreach (var stat in data.DailyStats.Values)
                    {
                        stat.HourlyActiveMinutes ??= new int[24];
                        stat.HourlyFocusSum ??= new int[24];
                        stat.HourlyFatigueSum ??= new int[24];
                        stat.HourlyMinutes ??= new int[24];
                        stat.StateCounts ??= new int[5];
                    }
                    service.RestoreDailyStats(data.DailyStats);
                }

                if (data.OverlayLeft.HasValue && data.OverlayTop.HasValue)
                {
                    service.UpdateOverlayPosition(data.OverlayLeft.Value, data.OverlayTop.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[데이터 로드 실패] {ex.Message}");
            }
        }

        public void Save(AnalysisEngine engine, MetricCollector collector, MonitoringService service)
        {
            try
            {
                var history = service.GetHistoryForSave();

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
                    PersonalVarMj = engine.PersonalVarMj,
                    FatigueScore = engine.FatigueScore,
                    ContinuousWorkMinutes = engine.ContinuousWorkMinutes,

                    FocusScore = engine.FocusScore,
                    FocusState = engine.FocusState,
                    StateReason = engine.StateReason,
                    IsFirstAnalysisComplete = engine.IsFirstAnalysisComplete,
                    HistoryScores = history.scores,
                    HistoryStates = history.states,
                    HistoryFatigue = history.fatigue,
                    DailyStats = service.DailyStats,

                    OverlayLeft = service.OverlayLeft,
                    OverlayTop = service.OverlayTop
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data, options));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[데이터 저장 실패] {ex.Message}");
            }
        }

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
            public double FatigueScore { get; set; }
            public int ContinuousWorkMinutes { get; set; }

            public int FocusScore { get; set; }
            public int FocusState { get; set; }
            public string StateReason { get; set; } = "";
            public bool IsFirstAnalysisComplete { get; set; }
            public List<int> HistoryScores { get; set; } = new();
            public List<int> HistoryStates { get; set; } = new();
            public List<int> HistoryFatigue { get; set; } = new();
            public Dictionary<string, DailyStat> DailyStats { get; set; } = new();

            public double? OverlayLeft { get; set; }
            public double? OverlayTop { get; set; }
        }
    }
}