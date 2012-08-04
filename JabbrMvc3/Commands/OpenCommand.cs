﻿using System;
using System.Web;
using System.Linq;
using JabbR.Models;

namespace JabbR.Commands
{
    [Command("open", "")]
    public class OpenCommand : UserCommand
    {
        public override void Execute(CommandContext context, CallerContext callerContext, ChatUser callingUser, string[] args)
        {
            if (args.Length == 0)
            {
                throw new InvalidOperationException("Which room do you want to open?");
            }

            string roomName = HttpUtility.HtmlDecode(args[0]);
            ChatRoom room = context.Repository.VerifyRoom(roomName, mustBeOpen: false);

            context.Service.OpenRoom(callingUser, room);

            // join the room, unless already in the room
            if (!room.Users.Contains(callingUser))
            {
                context.Service.JoinRoom(callingUser, room, inviteCode: null);

                context.Repository.CommitChanges();

                context.NotificationService.JoinRoom(callingUser, room);
            }

            var users = room.Users.ToList();
            
            context.NotificationService.UnCloseRoom(users, room);
        }
    }
}