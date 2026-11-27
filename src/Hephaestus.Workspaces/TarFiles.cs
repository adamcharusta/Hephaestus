using System.Formats.Tar;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces;

/// <summary>The tar archives the runtime sends to a container that is not running (Docker's copy-to-container).</summary>
internal static class TarFiles
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>
    /// The files, and the directories above them, owned by root and readable by every user and writable by none: a step container runs as a
    /// user of its own and mounts them read-only.
    /// </summary>
    public static MemoryStream ReadOnlyFiles(IEnumerable<WorkspaceFile> files)
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            var directories = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var names = file.Path.Split('/');
                for (var depth = 1; depth < names.Length; depth++)
                {
                    directories.Add(string.Join('/', names.Take(depth)));
                }
            }

            foreach (var directory in directories)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, directory) { Mode = DirectoryMode, Uid = 0, Gid = 0 });
            }

            foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, file.Path) { Mode = FileMode, Uid = 0, Gid = 0, DataStream = new MemoryStream(file.Content) });
            }
        }

        archive.Position = 0;
        return archive;
    }

    /// <summary>
    /// One directory entry, <paramref name="directory"/>, owned by <paramref name="userId"/> and <paramref name="groupId"/>. Extracted onto the
    /// mount point of a volume it sets the owner of the volume's root, which Docker gives to root when it creates the volume.
    /// </summary>
    public static MemoryStream OwnedDirectory(string directory, int userId, int groupId)
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, directory)
            {
                Mode = DirectoryMode | UnixFileMode.UserWrite,
                Uid = userId,
                Gid = groupId,
            });
        }

        archive.Position = 0;
        return archive;
    }
}
