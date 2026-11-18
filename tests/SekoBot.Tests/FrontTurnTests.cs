using SekoBot.Bot;
using SekoBot.Input;
using Xunit;

namespace SekoBot.Tests;

public class FrontTurnTests
{
    [Fact]
    public void One_bot_at_a_time_has_the_turn_and_the_next_gets_it_when_let_go()
    {
        string file = Path.Combine(Path.GetTempPath(), $"sira-{Guid.NewGuid():N}.lock");
        var one = new FrontTurn(file);
        var two = new FrontTurn(file);
        try
        {
            Assert.True(one.TryTake("KAiZER06"));
            Assert.True(one.TryTake("KAiZER06"));          // already its own
            Assert.True(one.Mine);
            Assert.False(two.TryTake("UNiCORN06"));          // waits
            Assert.Equal("KAiZER06", two.Holder());
            one.LetGo();
            Assert.False(one.Mine);
            Assert.True(two.TryTake("UNiCORN06"));
            Assert.False(one.TryTake("KAiZER06"));
        }
        finally
        {
            one.LetGo();
            two.LetGo();
            File.Delete(file);
            File.Delete(file + ".kimde");
        }
    }
}
