using Winter;

namespace WinterExtensions
{
    [WinterBotPlugin]
    public static class PluginLoader
    {
        public static void Init(WinterBot bot)
        {
            bot.AddCommands(new JukeBox(bot));
            bot.AddCommands(new BettingSystem(bot));
            bot.AddCommands(new ViewerCountLogger(bot));
            new ChatSaver(bot);
        }
    }
}
