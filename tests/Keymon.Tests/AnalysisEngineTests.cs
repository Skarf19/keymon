using Keymon;

namespace Keymon.Tests;

public class AnalysisEngineTests
{
    [Fact]
    public void PerformDeepAnalysis_WithNoActivity_ReportsIdle()
    {
        var engine = CreateEngine();

        engine.PerformDeepAnalysis(
            kpm: 0,
            mpm: 0,
            backspace: 0,
            maxConsecutiveBackspaces: 0,
            jerk: 0,
            csr: 0,
            avgDt: 100,
            avgFt: 400);

        Assert.Equal(0, engine.FocusState);
        Assert.Equal(0, engine.FocusScore);
    }

    [Fact]
    public void PerformDeepAnalysis_WithSustainedFastCleanInput_ReachesDeepFocus()
    {
        var engine = CreateEngine();

        for (int i = 0; i < 3; i++)
        {
            engine.PerformDeepAnalysis(
                kpm: 90,
                mpm: 5,
                backspace: 0,
                maxConsecutiveBackspaces: 0,
                jerk: 0,
                csr: 0,
                avgDt: 90,
                avgFt: 300);
        }

        Assert.Equal(4, engine.FocusState);
        Assert.True(engine.FocusScore >= 90);
    }

    [Fact]
    public void PerformDeepAnalysis_WithContinuousWork_AccumulatesFatigueSeparately()
    {
        var engine = CreateEngine();

        engine.PerformDeepAnalysis(
            kpm: 45,
            mpm: 5,
            backspace: 1,
            maxConsecutiveBackspaces: 1,
            jerk: 0,
            csr: 1,
            avgDt: 100,
            avgFt: 400);

        Assert.True(engine.FatigueScore > 0);
        Assert.InRange(engine.FatigueState, 1, 3);
        Assert.NotEqual(engine.FocusScore, (int)engine.FatigueScore);
    }

    [Fact]
    public void PerformDeepAnalysis_AfterFatigueAndRest_ReducesFatigue()
    {
        var engine = CreateEngine();

        for (int i = 0; i < 5; i++)
        {
            engine.PerformDeepAnalysis(
                kpm: 60,
                mpm: 5,
                backspace: 1,
                maxConsecutiveBackspaces: 1,
                jerk: 0,
                csr: 1,
                avgDt: 100,
                avgFt: 400);
        }

        double fatiguedScore = engine.FatigueScore;

        engine.PerformDeepAnalysis(
            kpm: 0,
            mpm: 0,
            backspace: 0,
            maxConsecutiveBackspaces: 0,
            jerk: 0,
            csr: 0,
            avgDt: 100,
            avgFt: 400);

        Assert.True(engine.FatigueScore < fatiguedScore);
    }

    private static AnalysisEngine CreateEngine()
    {
        return new AnalysisEngine
        {
            IsFirstAnalysisComplete = true,
            PersonalEmaKpm = 40,
            PersonalEmaEr = 0.05,
            PersonalEmaDt = 100,
            PersonalEmaFt = 400,
            PersonalEmaMj = 2,
            PersonalVarKpm = 100,
            PersonalVarEr = 0.01,
            PersonalVarDt = 100,
            PersonalVarFt = 100,
            PersonalVarMj = 4
        };
    }
}
