using Winter;

namespace WinterExtensions
{
    [WinterBotPlugin]
    public static class PluginLoader
    {
        public static void Init(WinterBot bot)
        {
            WinterOptions options = new WinterOptions(bot.Options);
            HttpManager.Instance.Options = options;

            bot.AddCommands(new JukeBox(bot, options));
            bot.AddCommands(new BettingSystem(bot, options));
            bot.AddCommands(new ViewerCountLogger(bot, options));
            new ChatSaver(bot, options);
            bot.AddCommands(new BetterCommands(bot, options));
        }
    }
}
