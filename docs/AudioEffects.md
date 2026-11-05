# Listening presets

Forma 1.1 adds six presets aimed at complete songs, alongside the original Dreamy spaces. Start with **Velvet Dream** for the slowed, softly glowing vocal sound associated with dreamy edits; try **Moonlit Cinema** when you want more space. The sound depends on the source mix: a track that already has heavy reverb usually needs less added ambience.

| Preset | Sound | Speed / pitch | Useful starting point |
| --- | --- | --- | --- |
| Velvet Dream | Soft warmth, modest chorus and a spacious vocal halo | 94% / slightly lower | Ballads, dream pop, Lana-inspired edits |
| Moonlit Cinema | Soft echoes and a larger, darker space | 90% / lower | Cinematic pop and atmospheric arrangements |
| Cassette Sunset | Rounded highs, gentle saturation and slow chorus | Original | Nostalgic pop, mellow guitars, softer synths |
| Underwater | Smooth, strongly muffled highs and diffuse ambience | 96% / slightly lower | Quiet, distant or late-night edits |
| Midnight Drive | A fuller low end, restrained echo and short room | Original | Pop and electronic tracks needing warmth without a big wash |
| Starlight | A light speed-up, pitch lift and short airy echo | 110% / higher | Bright pop and gentle sped-up edits |

These are design starting points, not exact recreations of a particular upload or artist's production. The edit format was informed by public examples such as [Art Deco — dreamy version / slowed + reverb](https://www.youtube.com/watch?v=utfhUvrYuA4). The signal processing uses [Pedalboard's documented filters, chorus, delay and reverb](https://spotify.github.io/pedalboard/reference/pedalboard.html). No third-party song audio is bundled.

All presets expose speed, reverb, filtering, stereo width and chorus in Advanced. New controls add high-pass filtering, echo time/mix, tape warmth, bass balance, chorus rate and tail duration. Speed changes pitch as well as duration, like changing a tape deck's speed; this is not pitch-preserving time stretching. Chorus creates gentle modulation; Cassette Sunset does not simulate every aspect of a physical cassette.

The new defaults use moderate wet mixes, bounded echo feedback and conservative stereo width. Their reverb/echo tails are rendered after the source ends and fade smoothly. Original preset values remain available; short edge fades prevent abrupt waveform boundaries. WAV processing targets -1 dBFS sample peak. This is not a guarantee of a particular perceived loudness or lossy true-peak level.

Preview processes the first 15 seconds of source plus its selected tail. Exports include the preset name in the filename. For comparisons, keep playback volume fixed; try several sections before choosing a preset for a whole song. If a dense mix sounds blurred, reduce Reverb wet and Echo mix first.

The audio quality fixture creates an original short chord progression and measures finite samples, headroom, duration, tails, edge fades, stereo correlation, processing-block boundaries, high-frequency attenuation, bass balance and silent-input behavior. These measurements catch technical problems; they do not replace a listener's judgment about musical taste.
