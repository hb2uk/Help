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

namespace JabbR.Models
{
    public partial class Member
    {
        public Member()
        {
            this.Member_CreditAccount = new HashSet<Member_CreditAccount>();
            this.Member_CreditTransaction = new HashSet<Member_CreditTransaction>();
            this.Member_FileArchive = new HashSet<Member_FileArchive>();
        }
    
        public int Id { get; set; }
        public System.Guid UserId { get; set; }
        public string User_Id { get; set; }
        public System.Guid Guid { get; set; }
        public int Agent_Id { get; set; }
        public string GenderId { get; set; }
        public bool IsDeleted { get; set; }
        public Nullable<System.Guid> Invitation_Code { get; set; }
        public string Reference_Code { get; set; }
        public Nullable<int> NotificationEnum { get; set; }
    
        public virtual ICollection<Member_CreditAccount> Member_CreditAccount { get; set; }
        public virtual ICollection<Member_CreditTransaction> Member_CreditTransaction { get; set; }
        public virtual ICollection<Member_FileArchive> Member_FileArchive { get; set; }
    }
    
}