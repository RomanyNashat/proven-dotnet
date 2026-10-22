namespace HomeReadings.Application;

/// <summary>
/// The texts the app shows, in the patient's language. Looked up by language code ("ar" or "en"), never
/// through CultureInfo, so they work on an image without ICU and never change how numbers or dates are read.
/// </summary>
public static class Messages
{
    public static string Saved(string language) => Pick(language, "تم حفظ القراءة", "Reading saved");

    public static string MeterOutOfRange(string language) => Pick(language,
        "الجهاز يعرض HI أو LO، أي أن القراءة خارج ما يستطيع قياسه. تواصل مع فريق الرعاية الآن، أو اتصل بالطوارئ إذا شعرت بتوعك.",
        "The meter shows HI or LO: the reading is outside what it can measure. Contact your care team now, or call emergency services if you feel unwell.");

    public static string InvalidValue(string language) => Pick(language,
        "أدخل الرقم الذي يعرضه الجهاز، مثل 6.2.",
        "Enter the number the meter shows, for example 6.2.");

    public static string InvalidUnit(string language) => Pick(language,
        "يجب أن تكون الوحدة mmol/L أو mg/dL.",
        "The unit must be mmol/L or mg/dL.");

    public static string InvalidNationalId(string language) => Pick(language,
        "يجب أن تتكون الهوية الوطنية من 10 أرقام.",
        "The national ID must be 10 digits.");

    public static string NationalIdMismatch(string language) => Pick(language,
        "رقم الهوية لا يطابق قراءاتك السابقة.",
        "This national ID doesn't match your earlier readings.");

    public static string NotesTooLong(string language) => Pick(language,
        "يمكن أن تصل الملاحظات إلى 500 حرف.",
        "Notes can be up to 500 characters.");

    public static string InvalidCollectedAt(string language) => Pick(language,
        "وقت القراءة مطلوب ولا يمكن أن يكون في المستقبل.",
        "The reading time is required and can't be in the future.");

    public static string InvalidPhoto(string language) => Pick(language,
        "يجب أن تكون الصورة بصيغة JPEG أو PNG أو HEIC أو WebP وبحجم لا يتجاوز 5 ميجابايت.",
        "The photo must be a JPEG, PNG, HEIC or WebP image of up to 5 MB.");

    private static string Pick(string language, string arabic, string english) => language == "ar" ? arabic : english;
}
