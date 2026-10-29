namespace LabResults.Application;

/// <summary>SMS texts. They name no test or value: the number isn't verified, and a test name is health data.</summary>
public static class SmsTemplates
{
    /// <summary>Arabic, under 70 characters so it fits one UCS-2 SMS part.</summary>
    public const string ResultReady = "لديك نتيجة تحليل جديدة. يمكنك الاطلاع عليها في التطبيق.";
}
