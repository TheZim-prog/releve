using System.Security.Cryptography;
using System.Text;
using Game.Core.Network;

namespace Game.Core.Tests.Network;

public class CodeVersionTests
{
    /// <summary>
    /// La version embarquée à la compilation (Game.Core.csproj) est bien l'empreinte des sources de Game.Core : tous
    /// les .cs hors bin/ et obj/, le projet et Directory.Build.props, par nom relatif, fins de ligne normalisées.
    /// </summary>
    [Fact]
    public void CodeVersion_IsTheHashOfTheCoreSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Releve.sln")))
            directory = directory.Parent;
        var repository = directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found.");
        var core = Path.Combine(repository, "src", "Game.Core");
        var sources = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Name: Path.GetRelativePath(core, path).Replace('\\', '/')))
            .Where(source => !source.Name.StartsWith("bin/") && !source.Name.StartsWith("obj/"))
            .Append((Path: Path.Combine(core, "Game.Core.csproj"), Name: "Game.Core.csproj"))
            .Append((Path: Path.Combine(repository, "Directory.Build.props"), Name: "Directory.Build.props"))
            .OrderBy(source => source.Name, StringComparer.Ordinal)
            .ToList();
        var text = new StringBuilder();
        foreach (var (path, name) in sources)
            text.Append(name).Append('\n').Append(File.ReadAllText(path).Replace("\r\n", "\n")).Append('\n');
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];

        Assert.True(sources.Count > 20, "The Game.Core sources were not found.");
        Assert.Equal(expected, Protocol.CodeVersion);
    }
}
