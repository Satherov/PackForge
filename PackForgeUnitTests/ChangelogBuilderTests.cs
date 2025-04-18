using PackForge.Core.Builders;

namespace PackForgeUnitTests
{
    public class ChangelogBuilderTests
    {
        [Theory]
        [InlineData("recipes/example.json", "example.json", SectionType.Recipes)]
        [InlineData("data/tags/info.json", "info.json", SectionType.Tags)]
        [InlineData("folder/registries/data.json", "data.json", SectionType.Registries)]
        [InlineData("loot_tables/chest.json", "chest.json", SectionType.LootTable)]
        [InlineData("other/path/file.json", "other/path/file.json", SectionType.Unknown)]
        public void GetSectionType_ReturnsExpectedSection(string path, string expectedTrim, SectionType expectedType)
        {
            (string trimmed, SectionType type) = ChangelogGenerator.GetSectionType(path);
            Assert.Equal(expectedTrim, trimmed);
            Assert.Equal(expectedType, type);
        }

        [Theory]
        [InlineData(SectionType.Recipes, "🍳 Recipes")]
        [InlineData(SectionType.Tags, "🏷️ Tags")]
        [InlineData(SectionType.Registries, "✍️ Registries")]
        [InlineData(SectionType.LootTable, "🗝️ Loot Tables")]
        [InlineData(SectionType.Unknown, "❓ Unknown")]
        public void PrintSectionHeader_ReturnsExpectedHeader(SectionType section, string expectedHeader)
        {
            string header = ChangelogGenerator.PrintSectionHeader(section);
            Assert.Equal(expectedHeader, header);
        }

        [Fact]
        public void StripJsonObjectValues_ReturnsSortedKeys()
        {
            const string json = "{\"b\": \"2\", \"a\": \"1\", \"c\": \"3\"}";
            
            string result = ChangelogGenerator.StripJsonObjectValues(json);
            Assert.Equal("a\nb\nc", result);
        }

        [Fact]
        public async Task FilesEqualAsync_WithIdenticalFiles_ReturnsTrue()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            string file1 = Path.Combine(tempDir, "file1.txt");
            string file2 = Path.Combine(tempDir, "file2.txt");
            const string content = "{\"a\": \"1\", \"b\": \"2\"}";
            await File.WriteAllTextAsync(file1, content);
            await File.WriteAllTextAsync(file2, content);

            bool equal = await ChangelogGenerator.JsonFilesEqualAsync(file1, file2, CancellationToken.None);
            Assert.True(equal);

            Directory.Delete(tempDir, true);
        }

        [Fact]
        public async Task FilesEqualAsync_WithDifferentFiles_ReturnsFalse()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            string file1 = Path.Combine(tempDir, "file1.txt");
            string file2 = Path.Combine(tempDir, "file2.txt");
            await File.WriteAllTextAsync(file1, "{\"a\": \"1\", \"b\": \"2\"}");
            await File.WriteAllTextAsync(file2, "{\"a\": \"2\", \"b\": \"1\"}");

            bool equal = await ChangelogGenerator.JsonFilesEqualAsync(file1, file2, CancellationToken.None);
            Assert.False(equal);

            Directory.Delete(tempDir, true);
        }

        [Fact]
        public void GroupBySection_GroupsEntriesCorrectly()
        {
            List<FileEntry> files =
            [
                new("recipes/example1.json", FileChangedType.Added),
                new("tags/info.json", FileChangedType.Changed),
                new("unknown/path/data.json", FileChangedType.Removed)
            ];

            Dictionary<SectionType, List<FileEntry>> groups = ChangelogGenerator.GroupBySection(files);

            Assert.True(groups.ContainsKey(SectionType.Recipes));
            Assert.True(groups.ContainsKey(SectionType.Tags));
            Assert.True(groups.ContainsKey(SectionType.Unknown));

            Assert.Single(groups[SectionType.Recipes]);
            Assert.Single(groups[SectionType.Tags]);
            Assert.Single(groups[SectionType.Unknown]);
        }

        [Fact]
        public void BuildEntry_WhenFilesDiffer_ReturnsNonEmptyDiff()
        {
            string tempOld = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string tempNew = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempOld);
            Directory.CreateDirectory(tempNew);

            const string relativePath = "folder/test.json";
            const string oldContent = "{\"key\": \"oldValue\"}";
            const string newContent = "{\"key\": \"newValue\"}";
            string oldFile = Path.Combine(tempOld, relativePath);
            string newFile = Path.Combine(tempNew, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);

            File.WriteAllText(oldFile, oldContent);
            File.WriteAllText(newFile, newContent);

            FileEntry entry = new(relativePath, FileChangedType.Changed);

            string diffOutput = ChangelogGenerator.BuildEntry(tempOld, tempNew, entry, SectionType.Unknown);
            Assert.False(string.IsNullOrWhiteSpace(diffOutput));
            Assert.Contains("+", diffOutput);

            Directory.Delete(tempOld, true);
            Directory.Delete(tempNew, true);
        }
        
        [Fact]
        public void BuildEntry_JsonObjectKeysOutOfOrder_ShouldBeUnchanged()
        {
            string tempOld = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string tempNew = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempOld);
            Directory.CreateDirectory(tempNew);

            const string relativePath = "folder/test.json";
            const string oldContent = "{\"tag\": \"#c:test_tag\", \"item\": \"minecraft:example\", \"other\": \"value\"}";
            const string newContent = "{\"item\": \"minecraft:example\", \"other\": \"value\", \"tag\": \"#c:test_tag\"}";

            string oldFile = Path.Combine(tempOld, relativePath);
            string newFile = Path.Combine(tempNew, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);

            File.WriteAllText(oldFile, oldContent);
            File.WriteAllText(newFile, newContent);

            FileEntry entry = new(relativePath, FileChangedType.Changed);

            string diffOutput = ChangelogGenerator.BuildEntry(tempOld, tempNew, entry, SectionType.Registries);

            Assert.DoesNotContain("+", diffOutput);
            Assert.DoesNotContain("-", diffOutput);

            Directory.Delete(tempOld, true);
            Directory.Delete(tempNew, true);
        }

        [Theory]
        [InlineData("""
                    {
                        key1: "value1",
                        key2: "value2",
                        key3: "value3"
                    }
                    """, """
                         {
                             key1: "value1",
                             key3: "value3",
                             key2: "value2"
                         }
                         """, true)]
        [InlineData("""
                    {
                        key1: "value1",
                        key2: "value2",
                        key4: "value4"
                    }
                    """, """
                         {
                             key4: "value4",
                             key1: "value1",
                             key2: "value2",
                         }
                         """, true)]
        [InlineData("""
                    {
                        key1: "value1",
                        key2: "value2",
                        key3: "value3"
                    }
                    """, """
                         {
                             key1: "value1",
                             key2: "value3",
                             key3: "value2"
                         }
                         """, false)]
        [InlineData("""
                    {
                        key1: "value1",
                        key2: "value2",
                        key3: "value3"
                    }
                    """, """
                         {
                             key1: "value1",
                             key2: "value2"
                         }
                         """, false)]
        [InlineData("""
                    {
                        key1: "value1",
                        key3: "value3"
                    }
                    """, """
                         {
                             key1: "value1",
                             key2: "value2"
                         }
                         """, false)]
        public async Task BuildEntry_FilesAreEqual_ReturnsCorrectResult(string oldContent, string newContent, bool equal)
        {
            string tempOld = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string tempNew = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempOld);
            Directory.CreateDirectory(tempNew);

            const string relativePath = "folder/test.json";

            string oldFile = Path.Combine(tempOld, relativePath);
            string newFile = Path.Combine(tempNew, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);

            await File.WriteAllTextAsync(oldFile, oldContent);
            await File.WriteAllTextAsync(newFile, newContent);

            bool diffOutput = await ChangelogGenerator.JsonFilesEqualAsync(oldFile, newFile);

            Assert.Equal(equal, diffOutput);

            Directory.Delete(tempOld, true);
            Directory.Delete(tempNew, true);
        }
    }
}
