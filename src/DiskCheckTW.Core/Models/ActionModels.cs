namespace DiskCheckTW.Core.Models;

/// <summary>給一般使用者的行動建議急迫程度。</summary>
public enum ActionUrgency { Immediate, Soon, Routine, CheckSetup }

public sealed class ActionStep
{
    public string Title { get; init; } = "";   // 例：「立刻備份重要資料」
    public string Detail { get; init; } = "";  // 白話說明，避免術語
}

/// <summary>「建議您接下來這樣做」：結論一句話＋依先後排序的行動。</summary>
public sealed class ActionPlan
{
    public ActionUrgency Urgency { get; init; }
    public string UrgencyText { get; init; } = "";   // 立即處理／近期處理／定期保養／先確認連接方式
    public string Headline { get; init; } = "";
    public IReadOnlyList<ActionStep> Steps { get; init; } = Array.Empty<ActionStep>();
}
