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
    public partial class Member_CreditTransaction
    {
        public int Id { get; set; }
        public int Member_Id { get; set; }
        public System.DateTime DateTimeStamp { get; set; }
        public int TransactionEnum { get; set; }
        public System.Guid ProcessBy { get; set; }
        public decimal Deposite { get; set; }
        public decimal withdraw { get; set; }
        public string Note { get; set; }
    
        public virtual Member Member { get; set; }
    }
    
}
