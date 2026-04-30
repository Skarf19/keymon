using System;
using System.Collections.Generic;

namespace Keymon
{
    public class AnalysisEngine
    {
        // 1. 상수 (판단 기준)
        private const double GlobalAvgKpm = 200.0;
        private const double GlobalAvgEr = 0.05;
        private const double GlobalAvgDt = 100.0;
        private const double GlobalAvgFt = 400.0;
        private const double GlobalAvgMj = 15.0;
        private const double Alpha = 0.1;

        // 2. 학습된 개인 지표
        public double PersonalEmaKpm { get; set; }
        public double PersonalEmaEr { get; set; }
        public double PersonalEmaDt { get; set; }
        public double PersonalEmaFt { get; set; }
        public double PersonalEmaMj { get; set; }
        public double PersonalVarKpm { get; set; }
        public double PersonalVarEr { get; set; }
        public double PersonalVarDt { get; set; }
        public double PersonalVarFt { get; set; }
        public double PersonalVarMj { get; set; }
        public int TotalAccumulatedKeys { get; set; }

        // 3. 현재 결과값
        public int FocusScore { get; private set; }
        public int StressScore { get; private set; }
        public int FocusState { get; private set; }
        public string StateReason { get; private set; } = "데이터 분석 중...";
        public bool IsFirstAnalysisComplete { get; set; } = false;

        // 4. 데이터 초기화 (사용자가 트레이에서 '초기화'를 선택했을 때 MonitoringService가 호출)
        // 학습된 모든 개인 베이스라인과 현재 분석 결과를 초기값으로 되돌립니다.
        public void Reset()
        {
            PersonalEmaKpm = 0; PersonalEmaEr = 0; PersonalEmaDt = 0;
            PersonalEmaFt = 0; PersonalEmaMj = 0;
            PersonalVarKpm = 0; PersonalVarEr = 0; PersonalVarDt = 0;
            PersonalVarFt = 0; PersonalVarMj = 0;
            TotalAccumulatedKeys = 0;
            FocusScore = 0; StressScore = 0; FocusState = 0;
            StateReason = "데이터 분석 중...";
            IsFirstAnalysisComplete = false;
        }

        // 5. 실시간 상태 판별 로직
        public void UpdateRealtimeStatus(int currentKpm, int currentMpm, int currentCsr, bool isFirstComplete)
        {
            int apm = currentKpm + currentMpm;
            if (apm < 15 && isFirstComplete)
            {
                FocusScore = 0;
                if (currentCsr >= 4)
                {
                    FocusState = 1; // Distracted
                    StateReason = $"현재 입력(APM {apm})이 부족한 반면, 창 전환이 발생하고 있어 산만한 상태로 판단됩니다.";
                }
                else
                {
                    FocusState = 0; // Idle
                    StateReason = $"현재 입력(APM {apm})이 감지되지 않아 작업이 일시 정지된 상태입니다.";
                }
                if (StressScore > 0) StressScore = Math.Max(0, StressScore - 5);
            }
        }

        // 5. 60초 주기 심층 분석 로직
        public void PerformDeepAnalysis(int kpm, int mpm, int backspace, int jerk, int csr, double avgDt, double avgFt)
        {
            int apm = kpm + mpm;
            double currentER = kpm > 0 ? (double)backspace / kpm : 0;
            double currentGamma = Math.Min(1.0, TotalAccumulatedKeys / 20000.0);

            // 베이스라인 결정
            double prevEmaKpm = PersonalEmaKpm == 0 ? GlobalAvgKpm : PersonalEmaKpm;
            double prevEmaEr = PersonalEmaEr == 0 ? GlobalAvgEr : PersonalEmaEr;
            double prevEmaDt = PersonalEmaDt == 0 ? GlobalAvgDt : PersonalEmaDt;
            double prevEmaFt = PersonalEmaFt == 0 ? GlobalAvgFt : PersonalEmaFt;
            double prevEmaMj = PersonalEmaMj == 0 ? GlobalAvgMj : PersonalEmaMj;

            // 표준편차 계산
            double stdKpm = Math.Max(Math.Sqrt(PersonalVarKpm), 5.0);
            double stdEr = Math.Max(Math.Sqrt(PersonalVarEr), 0.02);
            double stdDt = Math.Max(Math.Sqrt(PersonalVarDt), 10.0);
            double stdFt = Math.Max(Math.Sqrt(PersonalVarFt), 20.0);
            double stdMj = Math.Max(Math.Sqrt(PersonalVarMj), 2.0);

            // Z-Score 계산
            double zKpm = (kpm - prevEmaKpm) / stdKpm;
            double zEr = (currentER - prevEmaEr) / stdEr;
            double zDt = kpm > 0 ? (avgDt - prevEmaDt) / stdDt : 0;
            double zMj = (jerk - prevEmaMj) / stdMj;

            // 스트레스 및 집중도 점수 계산
            double combinedZ = (0.5 * zEr) + (0.3 * zMj) + (0.2 * zDt);
            StressScore = (int)Math.Clamp(Math.Max(0, combinedZ) * 33, 0, 100);

            // 상태 판별 알고리즘
            DetermineState(apm, csr, zKpm, zEr, zMj);

            // 집중도 최종 점수 계산
            double zErPositive = Math.Max(0, zEr);
            double erPenalty = zErPositive > 1.0 ? Math.Pow(zErPositive, 1.5) * 10 : zErPositive * 5;
            double csrPenalty = Math.Pow(csr, 1.5) * 1.5;
            double speedBonus = Math.Clamp((zKpm * 10) + (mpm * 0.1), -20, 25);
            double rawFocus = 75 + speedBonus - erPenalty - csrPenalty;
            FocusScore = apm < 15 ? 0 : (int)Math.Clamp(rawFocus, 0, 100);

            // 베이스라인 업데이트
            if (FocusState == 2 || FocusState == 3)
            {
                UpdateBaseline(kpm, currentER, avgDt, avgFt, jerk, prevEmaKpm, prevEmaEr, prevEmaDt, prevEmaFt, prevEmaMj);
            }
            IsFirstAnalysisComplete = true;
        }

        private void DetermineState(int apm, int csr, double zKpm, double zEr, double zMj)
        {
            if (apm < 15)
            {
                if (csr >= 6) { FocusState = 1; StateReason = $"입력 저조 및 창 전환 {csr}회 발생으로 방황 중."; }
                else { FocusState = 0; StateReason = "작업 흐름 정지 상태."; }
            }
            else if (csr >= 10 || zEr > 1.0 || zMj > 1.0)
            {
                FocusState = 1;
                if (csr >= 10) StateReason = "잦은 창 전환으로 인한 산만함.";
                else if (zEr > 1.0) StateReason = "비정상적인 오타율 급증.";
                else StateReason = "거친 마우스 움직임 감지.";
            }
            else if ((zKpm > 1.5 || apm >= 80) && zEr <= 0 && csr <= 2 && apm >= 50)
            {
                FocusState = 4; StateReason = "완벽한 몰입 상태!";
            }
            else if ((zKpm > 0.5 || apm >= 40) && csr <= 5 && apm >= 30)
            {
                FocusState = 3; StateReason = "안정적이고 빠른 작업 페이스 유지 중.";
            }
            else
            {
                FocusState = 2; StateReason = "평소 패턴과 일치하는 안정적인 상태.";
            }
        }

        private void UpdateBaseline(int kpm, double er, double dt, double ft, int jerk, double pKpm, double pEr, double pDt, double pFt, double pMj)
        {
            PersonalEmaKpm = (Alpha * kpm) + ((1 - Alpha) * pKpm);
            PersonalEmaEr = (Alpha * er) + ((1 - Alpha) * pEr);
            PersonalEmaDt = (Alpha * dt) + ((1 - Alpha) * pDt);
            PersonalEmaFt = (Alpha * ft) + ((1 - Alpha) * pFt);
            PersonalEmaMj = (Alpha * jerk) + ((1 - Alpha) * pMj);

            PersonalVarKpm = (1 - Alpha) * (PersonalVarKpm + Alpha * Math.Pow(kpm - pKpm, 2));
            PersonalVarEr = (1 - Alpha) * (PersonalVarEr + Alpha * Math.Pow(er - pEr, 2));
            PersonalVarDt = (1 - Alpha) * (PersonalVarDt + Alpha * Math.Pow(dt - pDt, 2));
            PersonalVarFt = (1 - Alpha) * (PersonalVarFt + Alpha * Math.Pow(ft - pFt, 2));
            PersonalVarMj = (1 - Alpha) * (PersonalVarMj + Alpha * Math.Pow(jerk - pMj, 2));
        }
    }
}
