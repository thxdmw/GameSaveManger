namespace GameSaveManager.Application.Discovery;

/// <summary>在本机绝对路径与可跨设备传输的 Windows 路径模板之间转换。</summary>
public interface ISavePathTemplateService
{
    string Encode(string path);
    string? Resolve(string pathTemplate);
}
