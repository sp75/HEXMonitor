//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace HEXMonitor
{
    using System;
    using System.Collections.Generic;
    
    public partial class HEX_Requisitions
    {
        public HEX_Requisitions()
        {
            this.HEX_Panels = new HashSet<HEX_Panels>();
        }
    
        public int id { get; set; }
        public long acc_id { get; set; }
        public Nullable<System.DateTime> date_processed { get; set; }
        public string patient_last_name { get; set; }
        public string patient_first_name { get; set; }
        public Nullable<System.DateTime> requisition_date { get; set; }
        public string hl7_data { get; set; }
    
        public virtual ICollection<HEX_Panels> HEX_Panels { get; set; }
    }
}
