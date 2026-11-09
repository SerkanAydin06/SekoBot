namespace SekoBot.Memory;

/// <summary>A conversation in the message (DM) window's list: the other player and where its entry is drawn.</summary>
internal readonly record struct DmPerson(string Name, Rectangle Rect);

/// <summary>A message showing in the open conversation. <paramref name="Mine"/>: written by us.</summary>
internal readonly record struct DmShown(string Name, string Message, bool Mine);

/// <summary>
/// The private message (DM) window as drawn (client pixels): open or not, the "Mesaj" button on the
/// left that opens and closes it, the conversations, the text box, the send button, and the messages
/// of the conversation showing.
/// </summary>
internal sealed record DmLayout(bool Open, Point? Opener, IReadOnlyList<DmPerson> People, Rectangle TextBox, Rectangle Send,
    IReadOnlyList<DmShown> Shown);
