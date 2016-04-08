﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PM.TaskBizInterface;
using PM.PaymentProtocolModel.BankCommModel;
using PM.TaskBiz.ORM;
using PM.Utils.Log;
using PM.Utils;
using System.Web;
using PM.Utils.WebUtils;

namespace PM.TaskBiz.HuangShanICBC
{
    /// <summary>
    /// 入账明细后续任务
    /// </summary>
    public class HuangShanICBCCallBack : ITimerTaskCallBack
    {
        public void CallBack(dynamic dyObj)
        {
            //   增量入库操作
            var haveIn = IncrementToDB(dyObj);
            //回调
            //  if (haveIn)  //控制暂时取消
            //匹配操作
            Match();
        }

        #region 增量入库
        /// <summary>
        /// 增量入库
        /// </summary>
        /// <param name="modelList">报文返回列表</param>
        /// <returns></returns>
        private bool IncrementToDB(List<ICBCQueryInfo> modelList)
        {
            bool rtn = false;
            T_Margin enter = null;
            var dbEnter = new PM.TaskBiz.ORM.Pub_Entities();
            var dbList = from c in dbEnter.T_Margin select c;
            //获取数据库内容
            foreach (var lst in modelList)
            {
                //通过流水号+缴费类型+授权码（类似标段编码）
                var chk = dbList.FirstOrDefault(p => p.HstSeqNum == lst.HstSeqNum && p.BusniessType == lst.BusniessType && p.AuthCode == lst.AuthCode&&p.BankType==lst.BankType);
                if (chk == null&&(!string.IsNullOrEmpty(lst.InAcct.Trim())))//增量添加
                {
                    enter = new T_Margin();
                    enter.ID = Guid.NewGuid().ToString();

                    enter.HstSeqNum = lst.HstSeqNum;
                    enter.InAcct = lst.InAcct;
                    enter.InAmount = lst.InAmount;
                    enter.InDate = lst.InDate;
                    enter.InMemo = lst.InMemo;
                    enter.InName = lst.InName;
                    enter.InTime = lst.InTime;
                    enter.PunInst = lst.PunInst;
                    enter.Gernal = lst.Gernal;
                    enter.BusniessType = lst.BusniessType;
                    enter.AuthCode = lst.AuthCode;
                    enter.SectionCode = lst.SectionCode;
                    enter.CreateTm = DateTime.Now;
                    enter.BankType = lst.BankType;
                    
                    dbEnter.T_Margin.AddObject(enter);
                    if (!rtn)
                    {
                        rtn = true;
                    }
                }
            }
            if (rtn)
            {
                try
                {
                    dbEnter.SaveChanges();
                }
                catch (Exception ex)
                {
                    LogTxt.WriteEntry("明细入账异常" + ex.Message, "黄山工行支付匹配");
                }

            }
            return rtn;
        }
        /// <summary>
        /// 匹配操作
        /// </summary>
        private void Match()
        {
            #region  回调准备
            var urlStr = ConfigHelper.GetCustomCfg("HuangShan", "BusinessUrl");
            var enCodingStr = ConfigHelper.GetCustomCfg("HuangShan", "enCoding");
            var chkStr = ConfigHelper.GetCustomCfg("HuangShan", "chkStr");//核对值
            var roundCount = Convert.ToInt32(ConfigHelper.GetCustomCfg("HuangShan", "roundCount"));//核对值
            if (string.IsNullOrEmpty(urlStr) || string.IsNullOrEmpty(enCodingStr) || string.IsNullOrEmpty(chkStr) || roundCount <= 0)
            {
                LogTxt.WriteEntry("回调地址或者编码等未设置，请设置", "黄山工行支付匹配");
                return;
            }
            var enCoding = Encoding.GetEncoding(enCodingStr);//回调编码
            #endregion

            bool haveMatch = false;//是否匹配 
            var dbEnter = new PM.TaskBiz.ORM.Pub_Entities();
            var matchList = dbEnter.T_Margin.Where(p => (p.Match != 1 || p.Match == null)&&p.BankType.ToLower()=="ICBC".ToLower());//获取匹配表待匹配信息 (获取借的标记   0借  1贷)
            #region 匹配处理  优先规则是订单号匹配到
            foreach (var lst in matchList)//匹配
            {
                var postStr = string.Format(
                    @"PayRealAccountName={0}&PayRealAccountNo={1}&PayRealBankName={2}
                        &ReceiveRealAccountName={3}&ReceiveRealAccountNo={4}&Amount={5}
                        &FeeAmount={6}&PrimaryID={7}&SlaveID={8}&TradeNo={9}
                        &SerialNumber={10}&LoanMark={11}&CostType={12}&PayDateTime={13}&PayDate={14}&PayTime={15}&&BankType={16}",
                   HttpUtility.UrlEncode(lst.InName ?? string.Empty, enCoding)
                   , lst.InAcct
                   , string.Empty
                   , HttpUtility.UrlEncode(lst.InName ?? string.Empty, enCoding)
                   , lst.InMemo
                   , lst.InAmount
                   , lst.PunInst//利息
                   , lst.SectionCode
                   , lst.AuthCode
                   , lst.HstSeqNum
                   , lst.HstSeqNum
                   , lst.BusniessType//借 0 贷1
                   , "BZJ"//费用类型
                   , lst.InDate + lst.InTime
                   , lst.InDate
                   , lst.InTime
                   ,lst.BankType
                   );
                LogTxt.WriteEntry("回调信息" + urlStr + postStr, "黄山工行支付匹配");
                if (HttpTransfer.PostBackToBusinesss(postStr, urlStr, enCoding, chkStr, roundCount))//匹配完成
                {
                    lst.Match = 1;//成功  
                    dbEnter.T_Margin.ApplyCurrentValues(lst);
                    haveMatch = true;//初始化匹配状态 
                }
                else
                {
                    LogTxt.WriteEntry("回调匹配响应失败" + postStr, "黄山工行支付匹配");
                }
            }
            if (haveMatch)//先保存一次
            {
                try
                {
                    dbEnter.SaveChanges();
                }
                catch (Exception ex)
                {
                    LogTxt.WriteEntry("订单匹配处理异常" + ex.Message, "黄山工行支付匹配");
                }
            }
            #endregion
        }
        #endregion
    }
}