using System;
using System.Globalization;
using System.IO;
using ProxyTrafficFloat;

public static class LocalizationTests
{
    public static void Run()
    {
        string previous = Strings.Language;
        var culture = CultureInfo.CurrentCulture;
        string path = Path.Combine(Path.GetTempPath(), "TrafficLens-language-" + Guid.NewGuid() + ".json");
        try
        {
            Strings.UseLanguage("zh-CN");
            if (Strings.IsEnglish || Strings.T("退出") != "退出") throw new Exception("Chinese translation failed");
            Strings.UseLanguage("en");
            if (!Strings.IsEnglish || Strings.T("退出") != "Exit" || Strings.T("未知") != "Unknown")
                throw new Exception("English translation failed");
            if (Strings.T("example.com") != "example.com" || Strings.Format("今日 {0}", "1 MB") != "Today 1 MB")
                throw new Exception("Display localization altered target values or templates");
            foreach (var message in new[] {
                "暂无符合当前设置的版本，可调整预览版选项。",
                "GitHub 请求受限，请稍后重试或打开版本页。",
                "连接超时，请检查网络或代理后重试。",
                "无法连接更新服务，请检查网络或代理。",
                "版本信息暂时不可用，请打开版本页。" })
                if (Strings.T(message) == message) throw new Exception("Update status is missing its English translation.");
            if (!ReferenceEquals(CultureInfo.CurrentCulture, culture)) throw new Exception("Language changed number formatting");

            Strings.UseLanguage("auto");
            bool expectedEnglish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "zh";
            if (Strings.IsEnglish != expectedEnglish) throw new Exception("Automatic UI language selection failed");
            Strings.UseLanguage("invalid");
            if (Strings.Language != "auto" || Strings.IsEnglish != expectedEnglish)
                throw new Exception("Invalid language did not fall back to automatic");

            new Settings { Language = "en" }.Save(path);
            if (Settings.Load(path).Language != "en") throw new Exception("Language preference was not saved");
            File.WriteAllText(path, "{\"Language\":\"invalid\"}");
            if (Settings.Load(path).Language != "auto") throw new Exception("Invalid saved language was not normalized");
            File.WriteAllText(path, "{}");
            if (Settings.Load(path).Language != "auto") throw new Exception("Old settings did not keep automatic language");
            if(Settings.Load(path).ColorMode!="system") throw new Exception("Old settings must follow the system theme.");
            new Settings { ColorMode="dark" }.Save(path);
            if(Settings.Load(path).ColorMode!="dark") throw new Exception("Theme preference was not saved.");
            AppTheme.Apply("light"); uint lightInk=AppTheme.Color("Ink");
            AppTheme.Apply("dark"); if(AppTheme.Color("Ink")==lightInk) throw new Exception("Theme palette did not change.");
            AppTheme.Apply("system");
            Console.WriteLine("PASS: Chinese/English catalog, system language, invalid fallback, unchanged culture, persisted language");
        }
        finally
        {
            Strings.UseLanguage(previous);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
