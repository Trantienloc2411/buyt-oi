using System.Xml.Linq;

namespace BuytOi.ArchitectureTests;

/// <summary>
/// Luật ADR 0001, kiểm tra trên <c>ProjectReference</c> của mọi .csproj (module/app mới tự được kiểm tra).
/// Project trong module đặt tên <c>BuytOi.&lt;Module&gt;.&lt;Lớp&gt;</c>.
/// </summary>
public sealed class ModuleDependencyTests
{
    private static readonly string Root = FindRoot();

    // Lớp → các lớp cùng module được phép tham chiếu. Mọi lớp được tham chiếu src/Shared.
    private static readonly Dictionary<string, string[]> SameModule = new()
    {
        ["Domain"] = [],
        ["Application"] = ["Domain", "Contracts"],
        ["Infrastructure"] = ["Application", "Domain", "Contracts"],
        ["Contracts"] = [],
    };

    // Lớp không được phụ thuộc ASP.NET Core (FrameworkReference) — chỉ Infrastructure là adapter HTTP.
    private static readonly string[] NoWebFramework = ["Domain", "Application", "Contracts"];

    private static readonly HashSet<string> Shared =
        Projects("src/Shared").Select(p => p.Name).ToHashSet();

    [Fact]
    public void Lop_trong_module_chi_tham_chieu_theo_luat()
    {
        var violations = new List<string>();
        foreach (var project in Projects("src/Modules"))
        {
            if (Layer(project.Name) is not { } layer || !SameModule.TryGetValue(layer, out var allowed))
            {
                violations.Add($"{project.Name}: tên phải là BuytOi.<Module>.<{string.Join('|', SameModule.Keys)}>");
                continue;
            }
            foreach (var reference in References(project.Path).Where(r => !Shared.Contains(r)))
            {
                var sameModule = Module(reference) == Module(project.Name);
                var ok = sameModule
                    ? allowed.Contains(Layer(reference))
                    : layer != "Domain" && Layer(reference) == "Contracts"; // module khác: chỉ qua Contracts
                if (!ok) violations.Add($"{project.Name} → {reference}");
            }
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void Domain_Application_Contracts_khong_phu_thuoc_ASP_NET_Core()
    {
        var violations =
            from project in Projects("src/Modules")
            where NoWebFramework.Contains(Layer(project.Name))
            where XDocument.Load(project.Path).Descendants("FrameworkReference").Any()
            select project.Name;
        Assert.Empty(violations);
    }

    [Fact]
    public void App_chi_tham_chieu_Infrastructure_Contracts_va_Shared()
    {
        var apps = Projects("src/Apps").ToList();
        var violations =
            from app in apps
            from reference in References(app.Path)
            where !Shared.Contains(reference) && Layer(reference) is not ("Infrastructure" or "Contracts")
            select $"{app.Name} → {reference}";

        Assert.NotEmpty(apps);
        Assert.Empty(violations);
    }

    private static string? Module(string project) => project.Split('.') is [_, var module, _] ? module : null;

    private static string? Layer(string project) => project.Split('.') is [_, _, var layer] ? layer : null;

    private static IEnumerable<string> References(string project) =>
        XDocument.Load(project).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value));

    private static IEnumerable<(string Name, string Path)> Projects(string directory) =>
        Directory.EnumerateFiles(Path.Combine(Root, directory), "*.csproj", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) == ".csproj")
            .Select(f => (Path.GetFileNameWithoutExtension(f), f));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BuytOi.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("Không tìm thấy BuytOi.slnx");
    }
}
