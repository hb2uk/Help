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
    public partial class Member_FileArchive
    {
        public int Id { get; set; }
        public int Member_Id { get; set; }
        public string Filename { get; set; }
        public string Extension { get; set; }
        public long Size { get; set; }
        public string MIME { get; set; }
        public int SortOrder { get; set; }
        public bool IsDefault { get; set; }
    
        public virtual Member Member { get; set; }
    }
    
}