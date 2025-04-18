using System.IO.Compression;
using PackForge.Core.Util;

namespace PackForgeUnitTests
{
    public class JarUtilTests : IDisposable
    {
        private readonly string _tempDirectory;

        public JarUtilTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        private static string CreateTestJar(string jarPath, bool includeModsToml, string? modsTomlContent = null, bool includeNestedJar = false)
        {
            using FileStream fs = new(jarPath, FileMode.Create);
            using ZipArchive archive = new(fs, ZipArchiveMode.Create);
            if (includeModsToml)
            {
                ZipArchiveEntry entry = archive.CreateEntry("META-INF/mods.toml");
                using StreamWriter writer = new(entry.Open());
                writer.Write(modsTomlContent ?? """
                                                [[mods]]
                                                displayName = "Test Mod"
                                                modId = "testmod"
                                                version = "1.0.0"
                                                authors = "Author1 Author2"
                                                """);
            }
            ZipArchiveEntry classEntry = archive.CreateEntry("com/example/HelloWorld.class");
            using (StreamWriter writer = new(classEntry.Open()))
            {
                writer.Write("// dummy class file");
            }
            if (!includeNestedJar) return jarPath;
            {
                ZipArchiveEntry nestedJarEntry = archive.CreateEntry("META-INF/jarjar/nested.jar");
                using Stream nestedStream = nestedJarEntry.Open();
                using ZipArchive nestedArchive = new(nestedStream, ZipArchiveMode.Create, leaveOpen: true);
                ZipArchiveEntry nestedModEntry = nestedArchive.CreateEntry("META-INF/mods.toml");
                using (StreamWriter writer = new(nestedModEntry.Open()))
                {
                    writer.Write("""
                                 [[mods]]
                                 displayName = "Nested Mod"
                                 modId = "nestedmod"
                                 version = "0.1.0"
                                 authors = "NestedAuthor, NestedAuthor2"
                                 """);
                }
                ZipArchiveEntry nestedClassEntry = nestedArchive.CreateEntry("org/example/NestedHello.class");
                using (StreamWriter writer = new(nestedClassEntry.Open()))
                {
                    writer.Write("// nested dummy class");
                }
            }

            return jarPath;
        }

        [Fact]
        public void ReadJarInfo_NonExistentFile_Throws()
        {
            Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await JarUtil.GetJarInfoInDirectoryAsync(Path.Combine(_tempDirectory, "missing")));
        }

        [Fact]
        public async Task ReadJarInfo_ValidJarWithToml_ParsesCorrectly()
        {
            string jarPath = Path.Combine(_tempDirectory, "test.jar");
            CreateTestJar(jarPath, true);

            List<ModInfo> modInfoList = await JarUtil.GetJarInfoInDirectoryAsync(_tempDirectory);
            Assert.Single(modInfoList);

            ModInfo info = modInfoList[0];
            Assert.Equal("testmod", info.Metadata.ModId);
            Assert.Equal("Test Mod", info.Metadata.DisplayName);
            Assert.Equal("1.0.0", info.Metadata.Version);
            Assert.Contains("Author1", info.Metadata.Authors);
            Assert.Contains("Author2", info.Metadata.Authors);
            Assert.False(info.OnlyJarInJars);
            Assert.True(info.ClassFileCount > 0);
        }

        [Fact]
        public async Task ReadJarInfo_WithoutToml_SetsOnlyJarInJarsCorrectly()
        {
            string jarPath = Path.Combine(_tempDirectory, "notoml.jar");
            CreateTestJar(jarPath, false);

            List<ModInfo> modInfoList = await JarUtil.GetJarInfoInDirectoryAsync(_tempDirectory);
            Assert.Single(modInfoList);

            ModInfo info = modInfoList[0];
            
            Assert.Equivalent(ModMetadata.Empty, info.Metadata);
        }

        [Fact]
        public async Task ReadJarInfo_WithNestedJar_ParsesNestedJarCorrectly()
        {
            string jarPath = Path.Combine(_tempDirectory, "jarinjar.jar");
            CreateTestJar(jarPath, true, includeNestedJar: true);

            List<ModInfo> modInfoList = await JarUtil.GetJarInfoInDirectoryAsync(_tempDirectory);
            Assert.Single(modInfoList);

            ModInfo parent = modInfoList[0];
            Assert.NotEmpty(parent.NestedJars);

            ModInfo nested = parent.NestedJars[0];
            Assert.Equal("nestedmod", nested.Metadata.ModId);
            Assert.Equal("Nested Mod", nested.Metadata.DisplayName);
            Assert.Equal("0.1.0", nested.Metadata.Version);
        }
    }
}
