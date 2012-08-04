using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using JabbR.Commands;
using JabbR.ContentProviders.Core;
using JabbR.Infrastructure;
using JabbR.Models;
using JabbR.Services;
using JabbR.ViewModels;
using Newtonsoft.Json;
using SignalR.Hubs;

namespace JabbR
{
    public class Chat : Hub, IConnected, IDisconnect, INotificationService
    {
        private readonly IJabbrRepository _repository;
        private readonly IChatService _service;
        private readonly ICache _cache;
        private readonly IResourceProcessor _resourceProcessor;
        private readonly IApplicationSettings _settings;

        private static readonly Version _version = typeof(Chat).Assembly.GetName().Version;
        private static readonly string _versionString = _version.ToString();

        public Chat(IApplicationSettings settings, IResourceProcessor resourceProcessor, IChatService service, IJabbrRepository repository, ICache cache)
        {
            _settings = settings;
            _resourceProcessor = resourceProcessor;
            _service = service;
            _repository = repository;
            _cache = cache;
        }

        private string UserAgent
        {
            get
            {
                if (Context.Headers != null)
                {
                    return Context.Headers["User-Agent"];
                }
                return null;
            }
        }

        private bool OutOfSync
        {
            get
            {
                string version = Caller.version;
                return String.IsNullOrEmpty(version) ||
                        new Version(version) != _version;
            }
        }

        public bool Join()
        {
            SetVersion();

            // Get the client state
            ClientState clientState = GetClientState();

            // Try to get the user from the client state
            ChatUser user = _repository.GetUserById(clientState.UserId);

            // Threre's no user being tracked
            if (user == null)
            {
                return false;
            }

            // Migrate all users to use new auth
            if (!String.IsNullOrEmpty(_settings.AuthApiKey) &&
                String.IsNullOrEmpty(user.Identity))
            {
                return false;
            }

            // Update some user values
            _service.UpdateActivity(user, Context.ConnectionId, UserAgent);
            _repository.CommitChanges();

            OnUserInitialize(clientState, user);

            return true;
        }

        private void SetVersion()
        {
            // Set the version on the client
            Caller.version = _versionString;
        }

        public bool CheckStatus()
        {
            bool outOfSync = OutOfSync;

            SetVersion();

            return outOfSync;
        }

        private void OnUserInitialize(ClientState clientState, ChatUser user)
        {
            // Update the active room on the client (only if it's still a valid room)
            if (user.Rooms.Any(room => room.Name.Equals(clientState.ActiveRoom, StringComparison.OrdinalIgnoreCase)))
            {
                // Update the active room on the client (only if it's still a valid room)
                Caller.activeRoom = clientState.ActiveRoom;
            }

            LogOn(user, Context.ConnectionId);
        }

        public bool Send(string content, string roomName)
        {
            var message = new ClientMessage
            {
                Content = content,
                Id = Guid.NewGuid().ToString("d"),
                Room = roomName
            };

            return Send(message);
        }

        public bool Send(ClientMessage message)
        {
            bool outOfSync = OutOfSync;

            SetVersion();

            // Sanitize the content (strip and bad html out)
            message.Content = HttpUtility.HtmlEncode(message.Content);

            // See if this is a valid command (starts with /)
            if (TryHandleCommand(message.Content, message.Room))
            {
                return outOfSync;
            }

            string id = GetUserId();

            ChatUser user = _repository.VerifyUserId(id);
            ChatRoom room = _repository.VerifyUserRoom(_cache, user, message.Room);

            // REVIEW: Is it better to use _repository.VerifyRoom(message.Room, mustBeOpen: false)
            // here?
            if (room.Closed)
            {
                throw new InvalidOperationException(String.Format("You cannot post messages to '{0}'. The room is closed.", message.Room));
            }

            // Update activity *after* ensuring the user, this forces them to be active
            UpdateActivity(user, room);


            HashSet<string> links;
            var messageText = ParseChatMessageText(message.Content, out links);

            ChatMessage chatMessage = _service.AddMessage(user, room, message.Id, messageText);


            var messageViewModel = new MessageViewModel(chatMessage);
            Clients[room.Name].addMessage(messageViewModel, room.Name);

            _repository.CommitChanges();

            string clientMessageId = chatMessage.Id;

            // Update the id on the message
            chatMessage.Id = Guid.NewGuid().ToString("d");
            _repository.CommitChanges();

            if (!links.Any())
            {
                return outOfSync;
            }

            ProcessUrls(links, room.Name, clientMessageId, chatMessage.Id);

            return outOfSync;
        }

        private string ParseChatMessageText(string content, out HashSet<string> links)
        {
            var textTransform = new TextTransform(_repository);
            string message = textTransform.Parse(content);
            return TextTransform.TransformAndExtractUrls(message, out links);
        }

        public UserViewModel GetUserInfo()
        {
            string id = GetUserId();

            ChatUser user = _repository.VerifyUserId(id);

            return new UserViewModel(user);
        }

        public Task Connect()
        {
            // We don't use this
            return null;
        }

        public Task Reconnect(IEnumerable<string> groups)
        {
            string id = GetUserId();

            if (String.IsNullOrEmpty(id))
            {
                return null;
            }

            ChatUser user = _repository.VerifyUserId(id);

            // Make sure this client is being tracked
            _service.AddClient(user, Context.ConnectionId, UserAgent);

            var currentStatus = (UserStatus)user.Status;

            if (currentStatus == UserStatus.Offline)
            {
                // Mark the user as inactive
                user.Status = (int)UserStatus.Inactive;
                _repository.CommitChanges();

                // If the user was offline that means they are not in the user list so we need to tell
                // everyone the user is really in the room
                var userViewModel = new UserViewModel(user);

                foreach (var room in user.Rooms)
                {
                    var isOwner = user.OwnedRooms.Contains(room);

                    // Tell the people in this room that you've joined
                    Clients[room.Name].addUser(userViewModel, room.Name, isOwner).Wait();

                    // Add the caller to the group so they receive messages
                    Groups.Add(Context.ConnectionId, room.Name).Wait();
                }
            }

            return null;
        }

        public Task Disconnect()
        {
            DisconnectClient(Context.ConnectionId);

            return null;
        }

        public object GetCommands()
        {
            return new[] {
                new { Name = "help", Description = "Type /help to show the list of commands" },
                new { Name = "nick", Description = "Type /nick [user] [password] to create a user or change your nickname. You can change your password with /nick [user] [oldpassword] [newpassword]" },
                new { Name = "join", Description = "Type /join [room] [inviteCode] - to join a channel of your choice. If it is private and you have an invite code, enter it after the room name" },
                new { Name = "create", Description = "Type /create [room] to create a room" },
                new { Name = "me", Description = "Type /me 'does anything'" },
                new { Name = "msg", Description = "Type /msg @nickname (message) to send a private message to nickname. @ is optional." },
                new { Name = "leave", Description = "Type /leave to leave the current room. Type /leave [room name] to leave a specific room." },
                new { Name = "where", Description = "Type /where [name] to list the rooms that user is in" },
                new { Name = "who", Description = "Type /who to show a list of all users, /who [name] to show specific information about that user" },
                new { Name = "list", Description = "Type /list (room) to show a list of users in the room" },
                new { Name = "gravatar", Description = "Type /gravatar [email] to set your gravatar." },
                new { Name = "invite", Description = "Type /invite [user] [room] - To invite a user to join a room." },
                new { Name = "nudge", Description = "Type /nudge to send a nudge to the whole room, or \"/nudge @nickname\" to nudge a particular user. @ is optional." },
                new { Name = "kick", Description = "Type /kick [user] to kick a user from the room. Note, this is only valid for owners of the room." },
                new { Name = "logout", Description = "Type /logout - To logout from this client (chat cookie will be removed)." },
                new { Name = "addowner", Description = "Type /addowner [user] [room] - To add an owner a user as an owner to the specified room. Only works if you're an owner of that room." },
                new { Name = "removeowner", Description = "Type /removeowner [user] [room] - To remove an owner from the specified room. Only works if you're the creator of that room." },
                new { Name = "lock", Description = "Type /lock [room] - To make a room private. Only works if you're the creator of that room." },
                new { Name = "open", Description = "Type /open [room] - To open a closed room. Only works if you're an owner of that room." },
                new { Name = "close", Description = "Type /close [room] - To close a room. Only works if you're an owner of that room." },
                new { Name = "allow", Description = "Type /allow [user] [room] - To give a user permission to a private room. Only works if you're an owner of that room." },
                new { Name = "unallow", Description = "Type /unallow [user] [room] - To revoke a user's permission to a private room. Only works if you're an owner of that room." },
                new { Name = "invitecode", Description = "Type /invitecode - To show the current invite code" },
                new { Name = "resetinvitecode", Description = "Type /resetinvitecode - To reset the current invite code. This will render the previous invite code invalid" },
                new { Name = "note", Description = "Type /note - To set a note shown via a paperclip icon next to your name, with the message appearing when you hover over it."},
                new { Name = "afk", Description = "Type /afk - (aka. Away From Keyboard). To set a temporary note shown via a paperclip icon next to your name, with the message appearing when you hover over it. This note will disappear when you first resume typing."},
                new { Name = "flag", Description = "Type /flag [Iso 3366-2 Code] - To show a small flag which represents your nationality. Eg. /flag US for a USA flag. ISO Reference Chart: http://en.wikipedia.org/wiki/ISO_3166-1_alpha-2 (Apologies to people with dual citizenship). "},
                new { Name = "topic", Description = "Type /topic [topic] to set the room topic. Type /topic to clear the room's topic." },
                new { Name = "welcome", Description = "Type /welcome [message] to set the room's welcome message. Type /welcome to clear the room's welcome message." },
                new { Name = "roomname", Description = "Type #roomname to add a link to that room in your message. Eg. If you add \"#meta\" to your message it will be replaced with a link to /#/rooms/meta." },
                new { Name = "broadcast",  Description = "Sends a message to all users in all rooms. Only administrators can use this command." }
            };
        }

        public IEnumerable<RoomViewModel> GetRooms()
        {
            string id = GetUserId();
            ChatUser user = _repository.VerifyUserId(id);

            var rooms = _repository.GetAllowedRooms(user).Select(r => new RoomViewModel
            {
                Name = r.Name,
                Count = r.Users.Count(u => u.Status != (int)UserStatus.Offline),
                Private = r.Private,
                Closed = r.Closed
            }).ToList();

            return rooms;
        }

        public IEnumerable<MessageViewModel> GetPreviousMessages(string messageId)
        {
            var previousMessages = (from m in _repository.GetPreviousMessages(messageId)
                                    orderby m.When descending
                                    select m).Take(100);


            return previousMessages.AsEnumerable()
                                   .Reverse()
                                   .Select(m => new MessageViewModel(m));
        }

        public RoomViewModel GetRoomInfo(string roomName)
        {
            if (String.IsNullOrEmpty(roomName))
            {
                return null;
            }

            ChatRoom room = _repository.GetRoomByName(roomName);

            if (room == null)
            {
                return null;
            }

            var recentMessages = (from m in _repository.GetMessagesByRoom(room)
                                  orderby m.When descending
                                  select m).Take(30).ToList();

            // Reverse them since we want to get them in chronological order
            recentMessages.Reverse();

            // Get online users through the repository
            IEnumerable<ChatUser> onlineUsers = _repository.GetOnlineUsers(room).ToList();

            return new RoomViewModel
            {
                Name = room.Name,
                Users = from u in onlineUsers
                        select new UserViewModel(u),
                Owners = from u in room.Owners.Online()
                         select u.Name,
                RecentMessages = recentMessages.Select(m => new MessageViewModel(m)),
                Topic = ConvertUrlsAndRoomLinks(room.Topic ?? ""),
                Welcome = ConvertUrlsAndRoomLinks(room.Welcome ?? ""),
                Closed = room.Closed
            };
        }

        private string ConvertUrlsAndRoomLinks(string message)
        {
            TextTransform textTransform = new TextTransform(_repository);
            message = textTransform.ConvertHashtagsToRoomLinks(message);
            HashSet<string> urls;
            return TextTransform.TransformAndExtractUrls(message, out urls);
        }

        // TODO: Deprecate
        public void Typing()
        {
            string roomName = Caller.activeRoom;

            Typing(roomName);
        }

        public void Typing(string roomName)
        {
            string id = GetUserId();
            ChatUser user = _repository.GetUserById(id);

            if (user == null)
            {
                return;
            }

            ChatRoom room = _repository.VerifyUserRoom(_cache, user, roomName);

            UpdateActivity(user, room);

            var userViewModel = new UserViewModel(user);
            Clients[room.Name].setTyping(userViewModel, room.Name);
        }

        private void LogOn(ChatUser user, string clientId)
        {
            // Update the client state
            Caller.id = user.Id;
            Caller.name = user.Name;
            Caller.hash = user.Hash;

            var userViewModel = new UserViewModel(user);
            var rooms = new List<RoomViewModel>();

            var ownedRooms = user.OwnedRooms.Select(r => r.Key);

            foreach (var room in user.Rooms)
            {
                var isOwner = ownedRooms.Contains(room.Key);

                // Tell the people in this room that you've joined
                Clients[room.Name].addUser(userViewModel, room.Name, isOwner).Wait();

                // Add the caller to the group so they receive messages
                Groups.Add(clientId, room.Name).Wait();

                // Add to the list of room names
                rooms.Add(new RoomViewModel
                {
                    Name = room.Name,
                    Private = room.Private,
                    Closed = room.Closed
                });
            }

            // Initialize the chat with the rooms the user is in
            Caller.logOn(rooms);
        }

        private void UpdateActivity(ChatUser user, ChatRoom room)
        {
            UpdateActivity(user);

            OnUpdateActivity(user, room);
        }

        private void UpdateActivity(ChatUser user)
        {
            _service.UpdateActivity(user, Context.ConnectionId, UserAgent);

            _repository.CommitChanges();
        }

        private void ProcessUrls(IEnumerable<string> links, string roomName, string clientMessageId, string messageId)
        {
            var contentTasks = links.Select(_resourceProcessor.ExtractResource).ToArray();
            Task.Factory.ContinueWhenAll(contentTasks, tasks =>
            {
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                    {
                        Trace.TraceError(task.Exception.GetBaseException().Message);
                        continue;
                    }

                    if (task.Result == null || String.IsNullOrEmpty(task.Result.Content))
                    {
                        continue;
                    }

                    // Try to get content from each url we're resolved in the query
                    string extractedContent = "<p>" + task.Result.Content + "</p>";

                    // Notify the room
                    Clients[roomName].addMessageContent(clientMessageId, extractedContent, roomName);

                    _service.AppendMessage(messageId, extractedContent);
                }
            });
        }

        private bool TryHandleCommand(string command, string room)
        {
            string clientId = Context.ConnectionId;
            string userId = GetUserId();

            var commandManager = new CommandManager(clientId, UserAgent, userId, room, _service, _repository, _cache, this);
            return commandManager.TryHandleCommand(command);
        }

        private void DisconnectClient(string clientId)
        {
            _service.DisconnectClient(clientId);
            
            // Sleep a little so that a browser refresh doesn't show the user 
            // coming offline and back online
            Thread.Sleep(500);

            // Query for the user to get the updated status
            ChatUser user = _repository.GetUserByClientId(clientId);

            // There's no associated user for this client id
            if (user == null)
            {
                return;
            }

            // The user will be marked as offline if all clients leave
            if (user.Status == (int)UserStatus.Offline)
            {
                foreach (var room in user.Rooms)
                {
                    var userViewModel = new UserViewModel(user);

                    Clients[room.Name].leave(userViewModel, room.Name).Wait();
                }
            }
        }

        private void OnUpdateActivity(ChatUser user, ChatRoom room)
        {
            var userViewModel = new UserViewModel(user);
            Clients[room.Name].updateActivity(userViewModel, room.Name);
        }

        private void LeaveRoom(ChatUser user, ChatRoom room)
        {
            var userViewModel = new UserViewModel(user);
            Clients[room.Name].leave(userViewModel, room.Name).Wait();

            foreach (var client in user.ConnectedClients)
            {
                Groups.Remove(client.Id, room.Name).Wait();
            }

            OnRoomChanged(room);
        }

        void INotificationService.LogOn(ChatUser user, string clientId)
        {
            LogOn(user, clientId);
        }

        void INotificationService.ChangePassword()
        {
            Caller.changePassword();
        }

        void INotificationService.SetPassword()
        {
            Caller.setPassword();
        }

        void INotificationService.KickUser(ChatUser targetUser, ChatRoom room)
        {
            foreach (var client in targetUser.ConnectedClients)
            {
                // Kick the user from this room
                Clients[client.Id].kick(room.Name);

                // Remove the user from this the room group so he doesn't get the leave message
                Groups.Remove(client.Id, room.Name).Wait();
            }

            // Tell the room the user left
            LeaveRoom(targetUser, room);
        }

        void INotificationService.OnUserCreated(ChatUser user)
        {
            // Set some client state
            Caller.name = user.Name;
            Caller.id = user.Id;
            Caller.hash = user.Hash;

            // Tell the client a user was created
            Caller.userCreated();
        }

        void INotificationService.JoinRoom(ChatUser user, ChatRoom room)
        {
            var userViewModel = new UserViewModel(user);
            var roomViewModel = new RoomViewModel
            {
                Name = room.Name,
                Private = room.Private,
                Welcome = ConvertUrlsAndRoomLinks(room.Welcome ?? ""),
                Closed = room.Closed
            };

            var isOwner = user.OwnedRooms.Contains(room);

            // Tell all clients to join this room
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].joinRoom(roomViewModel);
            }

            // Tell the people in this room that you've joined
            Clients[room.Name].addUser(userViewModel, room.Name, isOwner).Wait();

            // Notify users of the room count change
            OnRoomChanged(room);

            foreach (var client in user.ConnectedClients)
            {
                // Add the caller to the group so they receive messages
                Groups.Add(client.Id, room.Name).Wait();
            }
        }

        void INotificationService.AllowUser(ChatUser targetUser, ChatRoom targetRoom)
        {
            foreach (var client in targetUser.ConnectedClients)
            {
                // Tell this client it's an owner
                Clients[client.Id].allowUser(targetRoom.Name);
            }

            // Tell the calling client the granting permission into the room was successful
            Caller.userAllowed(targetUser.Name, targetRoom.Name);
        }

        void INotificationService.UnallowUser(ChatUser targetUser, ChatRoom targetRoom)
        {
            // Kick the user from the room when they are unallowed
            ((INotificationService)this).KickUser(targetUser, targetRoom);

            foreach (var client in targetUser.ConnectedClients)
            {
                // Tell this client it's an owner
                Clients[client.Id].unallowUser(targetRoom.Name);
            }

            // Tell the calling client the granting permission into the room was successful
            Caller.userUnallowed(targetUser.Name, targetRoom.Name);
        }

        void INotificationService.AddOwner(ChatUser targetUser, ChatRoom targetRoom)
        {
            foreach (var client in targetUser.ConnectedClients)
            {
                // Tell this client it's an owner
                Clients[client.Id].makeOwner(targetRoom.Name);
            }

            var userViewModel = new UserViewModel(targetUser);

            // If the target user is in the target room.
            // Tell everyone in the target room that a new owner was added
            if (_repository.IsUserInRoom(_cache, targetUser, targetRoom))
            {
                Clients[targetRoom.Name].addOwner(userViewModel, targetRoom.Name);
            }

            // Tell the calling client the granting of ownership was successful
            Caller.ownerMade(targetUser.Name, targetRoom.Name);
        }

        void INotificationService.RemoveOwner(ChatUser targetUser, ChatRoom targetRoom)
        {
            foreach (var client in targetUser.ConnectedClients)
            {
                // Tell this client it's no longer an owner
                Clients[client.Id].demoteOwner(targetRoom.Name);
            }

            var userViewModel = new UserViewModel(targetUser);

            // If the target user is in the target room.
            // Tell everyone in the target room that the owner was removed
            if (_repository.IsUserInRoom(_cache, targetUser, targetRoom))
            {
                Clients[targetRoom.Name].removeOwner(userViewModel, targetRoom.Name);
            }

            // Tell the calling client the removal of ownership was successful
            Caller.ownerRemoved(targetUser.Name, targetRoom.Name);
        }

        void INotificationService.ChangeGravatar(ChatUser user)
        {
            Caller.hash = user.Hash;

            // Update the calling client
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].gravatarChanged();
            }

            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Tell all users in rooms to change the gravatar
            foreach (var room in user.Rooms)
            {
                Clients[room.Name].changeGravatar(userViewModel, room.Name);
            }
        }

        void INotificationService.OnSelfMessage(ChatRoom room, ChatUser user, string content)
        {
            Clients[room.Name].sendMeMessage(user.Name, content, room.Name);
        }

        void INotificationService.SendPrivateMessage(ChatUser fromUser, ChatUser toUser, string messageText)
        {
            // Send a message to the sender and the sendee
            foreach (var client in fromUser.ConnectedClients)
            {
                Clients[client.Id].sendPrivateMessage(fromUser.Name, toUser.Name, messageText);
            }

            foreach (var client in toUser.ConnectedClients)
            {
                Clients[client.Id].sendPrivateMessage(fromUser.Name, toUser.Name, messageText);
            }
        }

        void INotificationService.PostNotification(ChatRoom room, ChatUser user, string message)
        {
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].postNotification(message, room.Name);
            }
        }

        void INotificationService.ListRooms(ChatUser user)
        {
            string userId = GetUserId();
            var userModel = new UserViewModel(user);

            Caller.showUsersRoomList(userModel, user.Rooms.Allowed(userId).Select(r => r.Name));
        }

        void INotificationService.ListUsers()
        {
            var users = _repository.Users.Online().Select(s => s.Name).OrderBy(s => s);
            Caller.listUsers(users);
        }

        void INotificationService.ListUsers(IEnumerable<ChatUser> users)
        {
            Caller.listUsers(users.Select(s => s.Name));
        }

        void INotificationService.ListUsers(ChatRoom room, IEnumerable<string> names)
        {
            Caller.showUsersInRoom(room.Name, names);
        }

        void INotificationService.LockRoom(ChatUser targetUser, ChatRoom room)
        {
            var userViewModel = new UserViewModel(targetUser);

            // Tell the room it's locked
            Clients.lockRoom(userViewModel, room.Name);

            // Tell the caller the room was successfully locked
            Caller.roomLocked(room.Name);

            // Notify people of the change
            OnRoomChanged(room);
        }

        void INotificationService.CloseRoom(IEnumerable<ChatUser> users, ChatRoom room)
        {
            // notify all members of room that it is now closed
            foreach (var user in users)
            {
                foreach (var client in user.ConnectedClients)
                {
                    Clients[client.Id].roomClosed(room.Name);
                }
            }
        }

        void INotificationService.UnCloseRoom(IEnumerable<ChatUser> users, ChatRoom room)
        {
            // notify all members of room that it is now re-opened
            foreach (var user in users)
            {
                foreach (var client in user.ConnectedClients)
                {
                    Clients[client.Id].roomUnClosed(room.Name);
                }
            }
        }

        void INotificationService.LogOut(ChatUser user, string clientId)
        {
            DisconnectClient(clientId);

            var rooms = user.Rooms.Select(r => r.Name);

            Caller.logOut(rooms);
        }

        void INotificationService.ShowUserInfo(ChatUser user)
        {
            string userId = GetUserId();

            Caller.showUserInfo(new
            {
                Name = user.Name,
                OwnedRooms = user.OwnedRooms
                    .Allowed(userId)
                    .Where(r => !r.Closed)
                    .Select(r => r.Name),
                Status = ((UserStatus)user.Status).ToString(),
                LastActivity = user.LastActivity,
                IsAfk = user.IsAfk,
                AfkNote = user.AfkNote,
                Note = user.Note,
                Hash = user.Hash,
                Rooms = user.Rooms.Allowed(userId).Select(r => r.Name)
            });
        }

        void INotificationService.ShowHelp()
        {
            Caller.showCommands();
        }

        void INotificationService.Invite(ChatUser user, ChatUser targetUser, ChatRoom targetRoom)
        {
            var transform = new TextTransform(_repository);
            string roomLink = transform.ConvertHashtagsToRoomLinks("#" + targetRoom.Name);

            // Send the invite message to the sendee
            foreach (var client in targetUser.ConnectedClients)
            {
                Clients[client.Id].sendInvite(user.Name, targetUser.Name, roomLink);
            }

            // Send the invite notification to the sender
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].sendInvite(user.Name, targetUser.Name, roomLink);
            }
        }

        void INotificationService.NugeUser(ChatUser user, ChatUser targetUser)
        {
            // Send a nudge message to the sender and the sendee
            foreach (var client in targetUser.ConnectedClients)
            {
                Clients[client.Id].nudge(user.Name, targetUser.Name);
            }

            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].sendPrivateMessage(user.Name, targetUser.Name, "nudged " + targetUser.Name);
            }
        }

        void INotificationService.NudgeRoom(ChatRoom room, ChatUser user)
        {
            Clients[room.Name].nudge(user.Name);
        }

        void INotificationService.LeaveRoom(ChatUser user, ChatRoom room)
        {
            LeaveRoom(user, room);
        }

        void INotificationService.OnUserNameChanged(ChatUser user, string oldUserName, string newUserName)
        {
            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Tell the user's connected clients that the name changed
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].userNameChanged(userViewModel);
            }

            // Notify all users in the rooms
            foreach (var room in user.Rooms)
            {
                Clients[room.Name].changeUserName(oldUserName, userViewModel, room.Name);
            }
        }

        void INotificationService.ChangeNote(ChatUser user)
        {
            bool isNoteCleared = user.Note == null;

            // Update the calling client
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].noteChanged(user.IsAfk, isNoteCleared);
            }

            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Tell all users in rooms to change the note
            foreach (var room in user.Rooms)
            {
                Clients[room.Name].changeNote(userViewModel, room.Name);
            }
        }

        void INotificationService.ChangeFlag(ChatUser user)
        {
            bool isFlagCleared = String.IsNullOrWhiteSpace(user.Flag);

            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Update the calling client
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].flagChanged(isFlagCleared, userViewModel.Country);
            }

            // Tell all users in rooms to change the flag
            foreach (var room in user.Rooms)
            {
                Clients[room.Name].changeFlag(userViewModel, room.Name);
            }
        }

        void INotificationService.ChangeTopic(ChatUser user, ChatRoom room)
        {
            bool isTopicCleared = String.IsNullOrWhiteSpace(room.Topic);
            var parsedTopic = ConvertUrlsAndRoomLinks(room.Topic ?? "");
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].topicChanged(isTopicCleared, parsedTopic);
            }
            // Create the view model
            var roomViewModel = new RoomViewModel
            {
                Name = room.Name,
                Topic = parsedTopic,
                Closed = room.Closed
            };
            Clients[room.Name].changeTopic(roomViewModel);
        }

        void INotificationService.ChangeWelcome(ChatUser user, ChatRoom room)
        {
            bool isWelcomeCleared = String.IsNullOrWhiteSpace(room.Welcome);
            var parsedWelcome = ConvertUrlsAndRoomLinks(room.Welcome ?? "");
            foreach (var client in user.ConnectedClients)
            {
                Clients[client.Id].welcomeChanged(isWelcomeCleared, parsedWelcome);
            }
        }

        void INotificationService.AddAdmin(ChatUser targetUser)
        {
            foreach (var client in targetUser.ConnectedClients)
            {
                // Tell this client it's an owner
                Clients[client.Id].makeAdmin();
            }

            var userViewModel = new UserViewModel(targetUser);

            // Tell all users in rooms to change the admin status
            foreach (var room in targetUser.Rooms)
            {
                Clients[room.Name].addAdmin(userViewModel, room.Name);
            }

            // Tell the calling client the granting of admin status was successful
            Caller.adminMade(targetUser.Name);
        }

        void INotificationService.RemoveAdmin(ChatUser targetUser)
        {
            foreach (var client in targetUser.ConnectedClients)
            {
                // Tell this client it's no longer an owner
                Clients[client.Id].demoteAdmin();
            }

            var userViewModel = new UserViewModel(targetUser);

            // Tell all users in rooms to change the admin status
            foreach (var room in targetUser.Rooms)
            {
                Clients[room.Name].removeAdmin(userViewModel, room.Name);
            }

            // Tell the calling client the removal of admin status was successful
            Caller.adminRemoved(targetUser.Name);
        }

        void INotificationService.BroadcastMessage(ChatUser user, string messageText)
        {
            // Tell all users in all rooms about this message
            foreach (var room in _repository.Rooms)
            {
                Clients[room.Name].broadcastMessage(messageText, room.Name);
            }
        }

        void INotificationService.ForceUpdate()
        {
            Clients.forceUpdate();
        }

        private void OnRoomChanged(ChatRoom room)
        {
            var roomViewModel = new RoomViewModel
            {
                Name = room.Name,
                Private = room.Private,
                Closed = room.Closed
            };

            // Update the room count
            Clients.updateRoomCount(roomViewModel, _repository.GetOnlineUsers(room).Count());
        }

        private string GetUserId()
        {
            ClientState state = GetClientState();
            return state.UserId;
        }

        private ClientState GetClientState()
        {
            // New client state
            var jabbrState = GetCookieValue("jabbr.state");

            ClientState clientState = null;

            if (String.IsNullOrEmpty(jabbrState))
            {
                clientState = new ClientState();
            }
            else
            {
                clientState = JsonConvert.DeserializeObject<ClientState>(jabbrState);
            }

            // Read the id from the caller if there's no cookie
            clientState.UserId = clientState.UserId ?? Caller.id;

            return clientState;
        }

        private string GetCookieValue(string key)
        {
            var cookie = Context.RequestCookies[key];
            string value = cookie != null ? cookie.Value : null;
            return value != null ? HttpUtility.UrlDecode(value) : null;
        }
    }
}
