using System.IO;
using RT.Util;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace DcsAutopilot;

public class Sound
{
    private MediaPlayer _player = new();
    private MediaSource _source;
    public string FileName { get; private set; }
    public int Volume { get; private set; }

    public Sound(string fileName, int volume = 100)
    {
        FileName = fileName;
        Volume = volume;
    }

    public void Play()
    {
        if (_player == null)
            return; // couldn't find the sound file
        if (_player.Source == null)
        {
            var fullname = Path.GetFullPath(FileName);
            if (!File.Exists(fullname))
            {
                fullname = PathUtil.AppPathCombine(FileName);
                if (!File.Exists(fullname))
                {
                    fullname = PathUtil.AppPathCombine("Sounds", FileName);
                    if (!File.Exists(fullname))
                    {
                        fullname = PathUtil.AppPathCombine("..", "..", "Sounds", FileName);
                        if (!File.Exists(fullname))
                        {
                            _player = null;
                            return;
                        }
                    }
                }
            }
            _source = MediaSource.CreateFromUri(new Uri("file:///" + fullname.Replace("\\", "/")));
            _player.CommandManager.IsEnabled = false;
            _player.Source = _source;
            _player.Volume = Volume / 100.0;
        }
        _player.Play();
    }
}
