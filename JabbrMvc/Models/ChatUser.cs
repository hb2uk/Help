//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using JabbR.Infrastructure;

namespace JabbR.Models
{
    public partial class ChatUser
    {
        public ChatUser()
        {
            ConnectedClients = new SafeCollection<ChatClient>();
            OwnedRooms = new SafeCollection<ChatRoom>();
            Rooms = new SafeCollection<ChatRoom>();
            AllowedRooms = new SafeCollection<ChatRoom>();
        }

        public int Key { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Hash { get; set; }
        public System.DateTime LastActivity { get; set; }
        public Nullable<System.DateTime> LastNudged { get; set; }
        public int Status { get; set; }
        public string HashedPassword { get; set; }
        public string Salt { get; set; }
        public string Note { get; set; }
        public string AfkNote { get; set; }
        public bool IsAfk { get; set; }
        public string Flag { get; set; }
        public string Identity { get; set; }
        public string Email { get; set; }
        public bool IsAdmin { get; set; }
        public int Member_Id { get; set; }
        public string DefaultImage { get; set; }

        public virtual ICollection<ChatClient> ConnectedClients { get; set; }
        public virtual ICollection<ChatMessage> Messages { get; set; }
        public virtual ICollection<ChatRoom> OwnedRooms { get; set; }
        public virtual ICollection<ChatRoom> Rooms { get; set; }
        public virtual ICollection<ChatRoom> AllowedRooms { get; set; }
    }

}
