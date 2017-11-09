using Labdaq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Compression;
using FormulaExcel;
using DocumentFormat.OpenXml.ReportBuilder;

namespace HEXMonitor
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
      //      dateTimePicker1.CustomFormat = "hh:mm";

       //     dateTimePicker1.Text = dateTimePicker1.Value.ToString();
           
        }

        private void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;
            progressBar1.Visible = true;
            var hl7_patch = textBox1.Text;
            var dt = Dt();

            string sql_orders = @"SELECT 
                    rq.ACC_ID,        
                    rq.PAT_ID,
                    rq.CREATED_DATE,
                    rq.DRAW_DATE,   
                    rq.RECEIVED_DATE,
                    rq.EXTERNAL_NO, 
                    rq.DIAGNOSIS_CODE1, 
                    rq.PI_ID1,
                    rq.PI_ID2,
                    rq.PI_ID3,
                    UPPER(p.F_NAME) F_NAME,
                    UPPER(p.L_NAME) L_NAME,
                    UPPER(p.M_NAME) M_NAME,
                    p.BIRTH,
                    p.GENDER,
                    p.ADDRESS1,
                    p.ADDRESS2,
                    p.CITY,
                    p.STATE,
                    p.ZIP,
                    p.SSN,
                    p.H_PHONE,
                    p.MARITAL_STATUS,
                    d.DOC_ID,
                    UPPER(d.F_NAME) as D_F_NAME,
                    UPPER(d.L_NAME) as D_L_NAME,
                    UPPER(d.M_NAME) as D_M_NAME,
                    d.ADDRESS1 as D_ADDRESS1

                FROM requisitions rq  
                inner join PATIENTS p on rq.PAT_ID = p.PAT_ID
                inner join  DOCTORS d on rq.DOC_ID1 = d.DOC_ID
                
                WHERE rq.DEL_FLAG='F' and rq.CREATED_DATE BETWEEN SYSDATE - {0} and SYSDATE 
                and ( exists (select rp.rp_id from RL_REQ_PANELS rp, RL_RESULTS res where rp.rp_id = res.rp_id and  rq.acc_id = rp.acc_id and res.STATUS='F' )
                     or exists (select rp.rp_id from REQ_PANELS rp, RESULTS res where rp.rp_id = res.rp_id and  rq.acc_id = rp.acc_id  )
                    ) 
                order by rq.PAT_ID ";

            string sql_panels = @"  select rp.RP_ID, rp.PROFILE_NAME as PANEL_NAME,rp.PROFILE_ID PANEL_ID, rp.CREATED_DATE, min(cpt.CPT_ID) CPT_ID, count(res.rp_id ) count_test
                from RL_REQ_PANELS rp
                inner join RL_RESULTS res on res.rp_id = rp.rp_id and res.DEL_FLAG='F'
                left outer join RL_REQ_PANEL_CPTS cpt on rp.RP_ID = cpt.RP_ID
                where rp.DEL_FLAG='F' and rp.ACC_ID  = {0} 
                group by rp.RP_ID, rp.PROFILE_NAME,rp.PROFILE_ID, rp.CREATED_DATE
             
                union all
             
                select rp.RP_ID, p.PANEL_NAME, TO_CHAR( rp.PANEL_ID) PANEL_ID, rp.CREATED_DATE, min(cpt.CPT_ID) CPT_ID, count(res.rp_id ) count_test
                from REQ_PANELS rp
                inner join PANELS p on rp.PANEL_ID = p.PANEL_ID
                inner join RESULTS res on  res.rp_id = rp.rp_id and res.DEL_FLAG='F'
                left outer join REQ_PANEL_CPTS cpt on rp.RP_ID = cpt.RP_ID
                where rp.DEL_FLAG='F' and rp.ACC_ID  = {0} 
                group by rp.RP_ID, p.PANEL_NAME,rp.PANEL_ID, rp.CREATED_DATE";

            int msg_count = 0;

            using (var lab = new LabdaqClient())
            {
                int step = 0;
                var d = DateTime.Now;
                List<Dictionary<string, object>> Req = lab.RunSql(String.Format(sql_orders, LastDays.Text), -1);
                int count_req = Req.Count();

                using (var db = new LENCO_WEBEntities())
                {
                    foreach (Dictionary<string, object> row in Req)
                    {
                        string ACC_ID = row["ACC_ID"].ToString();
                        string hl7_str = "";

                        long acc_id = Convert.ToInt32(row["ACC_ID"]);

                        var req = new HEX_Requisitions()
                         {
                             acc_id = acc_id,
                             date_processed = d,
                             patient_first_name = row["F_NAME"].ToString(),
                             patient_last_name = row["L_NAME"].ToString(),
                             requisition_date = Convert.ToDateTime(row["CREATED_DATE"].ToString())
                         };

                        int count = 0;
                        StringBuilder sb_pan = new StringBuilder();
                        List<Dictionary<string, object>> panels = lab.RunSql(String.Format(sql_panels, row["ACC_ID"]), -1);

                        if (panels.Any(a => a["PANEL_ID"].ToString() == "580") && panels.Any(a => a["PANEL_ID"].ToString() == "6700"))
                        {
                            panels.Remove(panels.FirstOrDefault(a => a["PANEL_ID"].ToString() == "580"));
                        }
                        if (panels.Any(a => a["PANEL_ID"].ToString() == "745") && panels.Any(a => a["PANEL_ID"].ToString() == "6700"))
                        {
                            panels.Remove(panels.FirstOrDefault(a => a["PANEL_ID"].ToString() == "745"));
                        }

                        foreach (Dictionary<string, object> p in panels)
                        {
                            int rp_id = Convert.ToInt32(p["RP_ID"]);
                            if (!db.HEX_Panels.Any(a => a.rp_id == rp_id))
                            {
                                req.HEX_Panels.Add(new HEX_Panels()
                                {
                                    acc_id = acc_id,
                                    panel_name = p["PANEL_NAME"].ToString(),
                                    rp_id = rp_id,
                                    panel_id = p["PANEL_ID"].ToString()
                                });

                                ++count;
                                sb_pan.Append(HL7(count, row, p));
                            }
                        }

                        if (count > 0)
                        {
                            //  sw.Write(HL7(lab, row));
                            //  sw.Write(sb_pan.ToString());
                            hl7_str += HL7(lab, row);
                            hl7_str += sb_pan.ToString();

                            req.hl7_data = hl7_str;

                            try
                            {
                                if (!String.IsNullOrEmpty(hl7_str))
                                {
                                    string fname = Path.Combine(hl7_patch, ACC_ID + ".hl7");
                                    StreamWriter sw = new StreamWriter(fname, true);
                                    sw.Write(hl7_str);
                                    sw.Close();
                                }

                                db.HEX_Requisitions.Add(req);
                                db.SaveChanges();

                                AddRow(dt, row);

                                ++msg_count;
                            }
                            catch
                            {
                                ;
                            }
                        }

                        Application.DoEvents();
                        progressBar1.Value = ++step * 100 / count_req;
                    }
                }

                label2.Text = d.ToString();
            }

            button1.Enabled = true;
            progressBar1.Visible = false;


            SendEmail("Requisitions count - " + msg_count.ToString(), BuildRep(dt));
        }

        private void SendEmail(String Body, String attachmentFilename)
        {
         //   String name = Path.GetFileName(attachmentFilename);
          //  ZipFile.CreateFromDirectory(Path.GetDirectoryName(attachmentFilename), @"arh\" + name + ".zip");
          //  File.Delete(attachmentFilename);
            try
            {
                MailMessage mail = new MailMessage();
                SmtpClient SmtpServer = new SmtpClient("mail.authsmtp.com");

                mail.From = new MailAddress("info@lencolab.com");
            //    mail.To.Add("sergeyp@4ib.com"); 
                mail.To.Add("felix@lencolab.com"); 
                mail.Subject = "HEX Monitor";
                mail.CC.Add("sergeyp@4ib.com");
                mail.Body = Body;

                SmtpServer.Port = 2525;
                SmtpServer.Credentials = new System.Net.NetworkCredential("ac60691", "fargsga8ptbnun");
               // SmtpServer.EnableSsl = true;

               if (attachmentFilename != null)
                {
                    mail.Attachments.Add(new Attachment(attachmentFilename));
                }

                SmtpServer.Send(mail);
              
            }
            catch (Exception ex)
            {
                ;
            }
        }

        private string BuildRep(DataTable dt)
        {
            string output_file = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Rep", DateTime.Now.ToLongDateString() +"_"+ DateTime.Now.Ticks.ToString() + ".xlsx");
            var ds = new DataSet();
            ds.Tables.Add(dt );

            CalculationlFormulaExcel.CalcSpreadsheetDocument(
                ReportBuilderXLS.GenerateReport(
                    ds,
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Template", "HexLog.xlsx")),
                true, output_file);

            return output_file;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            var dt = DateTime.Now;
            if (dt.Minute == dateTimePicker1.Value.Minute && dt.Hour == dateTimePicker1.Value.Hour && dt.Second == dateTimePicker1.Value.Second)
            {
                button1.PerformClick();
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            Properties.Settings.Default.Save();
        }

        private String HL7(LabdaqClient lc, Dictionary<string, object> pat)
        {
            var dt = DateTime.Now;
            StringBuilder sb = new StringBuilder();

            string birth = String.IsNullOrEmpty(pat["BIRTH"].ToString()) ? "" : DateTime.Parse(pat["BIRTH"].ToString()).ToString("yyyyMMdd");

            sb.AppendLine(String.Format(@"MSH|^~\&|LABDAQ||||{0}||ORU^R01|{1}|P|2.4|||||||||", dt.ToString("yyyyMMddHHmm"), dt.ToString("yyyyMMddHHmmssfff")));
            sb.AppendLine(String.Format(@"PID|1||{0}||{1}^{2}^{3}^||{4}|{5}|||{6}^{7}^{8}^{9}^{10}||{11}|||{12}|||{13}|||||||||||||||||",
                pat["PAT_ID"], pat["L_NAME"], pat["F_NAME"], pat["M_NAME"], birth, pat["GENDER"], pat["ADDRESS1"], pat["ADDRESS2"], pat["CITY"], 
                pat["STATE"], pat["ZIP"], pat["H_PHONE"].ToString().Replace("(", "").Replace(")", "").Replace("-", ""), pat["MARITAL_STATUS"], pat["SSN"].ToString().Replace("-","")));

            int idx = 0;
            foreach (var item in GetIns(lc, pat))
            {
                string EFFECTIVE_DATE = String.IsNullOrEmpty(item["EFFECTIVE_DATE"].ToString()) ? "" : DateTime.Parse(item["EFFECTIVE_DATE"].ToString()).ToString("yyyyMMdd");

                sb.AppendLine(String.Format(@"IN1|{0}||{1}^|{2}|^^^^| ||||||{14}|||Z|{3}^{4}^{5}||{6}|{7}^{8}^{9}^{10}^{11}|||{12}||||||||||||||{13}|||||||F||||",
                    ++idx, item["INS_ID"], item["INS_NAME"], item["L_NAME"], item["F_NAME"], item["M_NAME"], birth, item["ADDRESS1"], item["ADDRESS2"], item["CITY"], item["STATE"], item["ZIP"], item["PRIORITY"], item["POLICY_NO"], EFFECTIVE_DATE));

                sb.AppendLine(String.Format(@"IN2||{0}|||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||{1}|",pat["SSN"].ToString().Replace("-",""),  pat["H_PHONE"].ToString().Replace("(", "").Replace(")", "").Replace("-", "")));
            }

            return sb.ToString();
        }

        private String HL7(int obr, Dictionary<string, object> pat, Dictionary<string, object> panel)
        {
            StringBuilder sb = new StringBuilder();

            string draw_date = String.IsNullOrEmpty(pat["DRAW_DATE"].ToString()) ? "" : DateTime.Parse(pat["DRAW_DATE"].ToString()).ToString("yyyyMMddHHmm"); 
            string received_date =  String.IsNullOrEmpty(pat["RECEIVED_DATE"].ToString()) ? "" :  DateTime.Parse(pat["RECEIVED_DATE"].ToString()).ToString("yyyyMMddHHmm");

            sb.AppendLine(String.Format(@"ORC|RE|{0}^LABDAQ|{1}||||||{2}|||{3}^{4}^{5} ^{6}^^^^P||||^|", pat["ACC_ID"], pat["EXTERNAL_NO"], DateTime.Parse(pat["CREATED_DATE"].ToString()).ToString("yyyyMMddHHmm"),
                    pat["DOC_ID"], pat["D_L_NAME"], pat["D_F_NAME"], pat["D_ADDRESS1"]));

            sb.AppendLine(String.Format(@"OBR|{0}|{1}|{2}|{3}^{4}^L^{11}^^CPT|||{5}|||~AV|A|||{6}||{7}^{8}^{9}^{10}^^^^P|||||||||F||1^^^^^R^^^||||||||||", obr, pat["ACC_ID"], pat["EXTERNAL_NO"], panel["PANEL_ID"], 
                panel["PANEL_NAME"], draw_date, received_date, pat["DOC_ID"], pat["D_L_NAME"], pat["D_F_NAME"], pat["D_ADDRESS1"], panel["CPT_ID"]));

            if (!String.IsNullOrEmpty(Convert.ToString(pat["DIAGNOSIS_CODE1"])))
            {
                sb.AppendLine(String.Format(@"DG1|{0}|I10|{1}|{2}|{3}||||||||||", obr, pat["DIAGNOSIS_CODE1"], "", DateTime.Parse(panel["CREATED_DATE"].ToString()).ToString("yyyyMMddHHmm")));
            }

            return sb.ToString();
        }

        private List<Dictionary<string, object>> GetIns(LabdaqClient lc, Dictionary<string, object> pat)
        {
            string sql = @"select p.*, ins.INS_NAME  from PAT_INSURANCE p
                           inner join INS_COMPANIES ins ON p.INS_ID = ins.INS_ID 
                           where p.PAT_ID  = '{0}' and ( PI_ID = {1} or PI_ID = {2} or PI_ID = {3})
                           order by p.PRIORITY";

            sql = String.Format(sql, pat["PAT_ID"], !String.IsNullOrEmpty(pat["PI_ID1"].ToString()) ? pat["PI_ID1"] : -1, !String.IsNullOrEmpty(pat["PI_ID2"].ToString()) ? pat["PI_ID2"] : -1, !String.IsNullOrEmpty(pat["PI_ID3"].ToString()) ? pat["PI_ID3"] : -1);

            return lc.RunSql(sql, -1);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            var fDialog = new FolderBrowserDialog();
            fDialog.SelectedPath = textBox1.Text;
            fDialog.ShowDialog();
            textBox1.Text = fDialog.SelectedPath;
        }

        private DataTable Dt()
        {
            var dt = new DataTable("range1");
            dt.Columns.Add("ACC_ID");
            dt.Columns.Add("CREATED_DATE");
            dt.Columns.Add("F_NAME");
            dt.Columns.Add("L_NAME");
            dt.Columns.Add("BIRTH");
            dt.Columns.Add("D_F_NAME");
            dt.Columns.Add("D_L_NAME");
            dt.Columns.Add("D_ADDRESS1");
            return dt;
        }

        private void AddRow(DataTable dt, Dictionary<string, object> row)
        {
            var r = dt.NewRow();
            r["ACC_ID"] = row["ACC_ID"];
            r["CREATED_DATE"] = row["CREATED_DATE"];
            r["F_NAME"] = row["F_NAME"];
            r["L_NAME"] = row["L_NAME"];
            r["BIRTH"] = row["BIRTH"];
            r["D_F_NAME"] = row["D_F_NAME"];
            r["D_L_NAME"] = row["D_L_NAME"];
            r["D_ADDRESS1"] = row["D_ADDRESS1"];
            dt.Rows.Add(r);

            dt.AcceptChanges();
        }

     }
}
