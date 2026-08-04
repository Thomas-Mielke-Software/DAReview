namespace DarkAmbientRadio.Core.Audio;

/// <summary>
/// Opens a file for TagLib denying nothing: the player holds the currently playing track open and
/// a Nextcloud prefetch or a rename may be in flight. TagLib's own abstraction insists on
/// <see cref="FileShare.Read"/> and would throw in exactly those cases.
/// </summary>
internal sealed class SharedFileAbstraction : TagLib.File.IFileAbstraction
{
    public SharedFileAbstraction(string path) => Name = path;

    public string Name { get; }

    public Stream ReadStream => Open();

    public Stream WriteStream => throw new NotSupportedException("Read-only abstraction.");

    public void CloseStream(Stream stream) => stream.Dispose();

    private FileStream Open() => new(
        Name, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
}
