using System.Text.Encodings.Web;
using System.Text.Json;

namespace MiniAppBox;

/// <summary>
/// JSON 序列化选项：非 ASCII 字符不转义，与手机端 org.json JSONObject.toString 输出一致，
/// JS 侧 JSON.parse 行为也完全相同。
/// </summary>
internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
