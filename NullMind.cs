using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

class Program
{
    private DiscordSocketClient _client;
    private Random rand = new Random();

    // --- SANITY SYSTEM ---
    private Dictionary<ulong, int> sanity = new Dictionary<ulong, int>();
    private List<ulong> priorityUsers = new List<ulong>();
    private ulong ownerId;
    private IUserMessage sanityMessage;
    private int currentPage = 0;
    private const int usersPerPage = 10;

    static void Main(string[] args)
        => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers
        });

        _client.Log += msg =>
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        };

        _client.MessageReceived += HandleCommand;
        _client.InteractionCreated += HandleInteraction;

        // --- GET TOKEN AND OWNER ID FROM ENV VARIABLES ---
        string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
        ownerId = ulong.Parse(Environment.GetEnvironmentVariable("OWNER_ID"));

        // --- START BOT ---
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        // --- TINY WEB SERVER TO KEEP BOT ALIVE ---
        Task.Run(() =>
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://*:8080/");
            listener.Start();
            Console.WriteLine("Web server started to keep bot alive.");

            while (true)
            {
                var context = listener.GetContext();
                var response = context.Response;
                string responseString = "Bot is alive!";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        });

        Console.WriteLine("Bot is running!");
        await Task.Delay(-1);
    }

    // --- SANITY HELPERS ---
    private int GetSanity(ulong userId)
    {
        if (!sanity.ContainsKey(userId))
            sanity[userId] = 100;
        return sanity[userId];
    }

    private async Task ChangeSanity(ulong userId, int amount)
    {
        sanity[userId] = Math.Clamp(GetSanity(userId) + amount, 0, 100);
        await UpdateSanityMessage();
    }

    // --- SANITY EMBED ---
    private Embed BuildSanityEmbed()
    {
        var builder = new EmbedBuilder()
            .WithTitle("🧠 Sanity Board")
            .WithColor(Discord.Color.DarkPurple);

        // ensure owner on top
        if (!priorityUsers.Contains(ownerId))
            priorityUsers.Insert(0, ownerId);

        string priorityText = "";
        string normalText = "";

        foreach (var id in priorityUsers)
        {
            int s = GetSanity(id);
            string state = s > 70 ? "🟢" : s > 30 ? "🟡" : "🔴";
            priorityText += $"<@{id}> → {s} {state}\n";
        }

        var others = sanity.Where(x => !priorityUsers.Contains(x.Key))
                           .OrderBy(x => x.Value)
                           .ToList();

        int totalPages = Math.Max(1, (int)Math.Ceiling(others.Count / (double)usersPerPage));
        currentPage = Math.Clamp(currentPage, 0, totalPages - 1);

        var pageUsers = others.Skip(currentPage * usersPerPage)
                              .Take(usersPerPage);

        foreach (var user in pageUsers)
        {
            string state = user.Value > 70 ? "🟢" : user.Value > 30 ? "🟡" : "🔴";
            normalText += $"<@{user.Key}> → {user.Value} {state}\n";
        }

        if (string.IsNullOrEmpty(normalText)) normalText = "No data.";

        builder.AddField("⭐ Priority", string.IsNullOrEmpty(priorityText) ? "None" : priorityText);
        builder.AddField($"📊 Everyone Else (Page {currentPage + 1}/{totalPages})", normalText);

        return builder.Build();
    }

    private MessageComponent BuildButtons()
    {
        return new ComponentBuilder()
            .WithButton("⬅️", "prev_page", ButtonStyle.Primary)
            .WithButton("➡️", "next_page", ButtonStyle.Primary)
            .Build();
    }

    private async Task UpdateSanityMessage()
    {
        if (sanityMessage != null)
        {
            await sanityMessage.ModifyAsync(msg =>
            {
                msg.Embed = BuildSanityEmbed();
                msg.Components = BuildButtons();
            });
        }
    }

    // --- HANDLE MESSAGE COMMANDS ---
    private async Task HandleCommand(SocketMessage message)
    {
        if (!(message is SocketUserMessage msg)) return;
        if (msg.Author.IsBot) return;

        string content = msg.Content.ToLower();
        ulong userId = msg.Author.Id;

        // --- TEST COMMAND ---
        if (content == "!ping")
        {
            await msg.Channel.SendMessageAsync("pong 🏓");
            return;
        }

        // --- SANITY BOARD ---
        if (content.StartsWith("!sanity"))
        {
            if (msg.Author.Id != ownerId)
            {
                await msg.Channel.SendMessageAsync("You are not allowed to use this.");
                return;
            }

            sanityMessage = await msg.Channel.SendMessageAsync(
                embed: BuildSanityEmbed(),
                components: BuildButtons()
            );
        }

        // --- PIN PRIORITY USER ---
        else if (content.StartsWith("!pin") && msg.MentionedUsers.Count > 0)
        {
            if (msg.Author.Id != ownerId) return;
            ulong id = msg.MentionedUsers.First().Id;
            if (!priorityUsers.Contains(id))
            {
                priorityUsers.Add(id);
                await msg.Channel.SendMessageAsync("User pinned.");
                await UpdateSanityMessage();
            }
        }

        // --- UNPIN PRIORITY USER ---
        else if (content.StartsWith("!unpin") && msg.MentionedUsers.Count > 0)
        {
            if (msg.Author.Id != ownerId) return;
            ulong id = msg.MentionedUsers.First().Id;
            if (priorityUsers.Contains(id))
            {
                priorityUsers.Remove(id);
                await msg.Channel.SendMessageAsync("User unpinned.");
                await UpdateSanityMessage();
            }
        }

        // --- SANITY CHECK ---
        else if (content.StartsWith("!check"))
        {
            int s = GetSanity(userId);
            await msg.Channel.SendMessageAsync($"🧠 Your sanity: {s}");
        }

        // --- SANITY INFLUENCED CHATBOT ---
        else if (content.StartsWith("!ask"))
        {
            string[] responses = { "Yes.", "No.", "Maybe.", "I don't know." };
            string answer = responses[rand.Next(responses.Length)];
            await msg.Channel.SendMessageAsync(answer);

            int userSanity = GetSanity(userId);
            string dmMessage = userSanity > 70 ? "Everything seems normal."
                             : userSanity > 30 ? "Something feels... off."
                             : "It is lying to you.";

            try { await msg.Author.SendMessageAsync($"👁️ {dmMessage}"); } catch { }

            await ChangeSanity(userId, -2);
        }
    }

    // --- HANDLE INTERACTIONS (BUTTONS / SELECT MENUS) ---
    private async Task HandleInteraction(SocketInteraction interaction)
    {
        if (interaction is SocketMessageComponent component)
        {
            if (component.Data.CustomId == "prev_page")
                currentPage--;
            else if (component.Data.CustomId == "next_page")
                currentPage++;

            await component.UpdateAsync(msg =>
            {
                msg.Embed = BuildSanityEmbed();
                msg.Components = BuildButtons();
            });
        }
    }
}
