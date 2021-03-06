﻿using CoinbaseExchange.NET.Core;
using CoinbaseExchange.NET.CoreGenerics;
using CoinbaseExchange.NET.Endpoints;
using CoinbaseExchange.NET.Endpoints.Account;
using CoinbaseExchange.NET.Endpoints.Deposits;
using CoinbaseExchange.NET.Endpoints.Orders;
using GdaxHoarder.Data;
using GdaxHoarder.Data.Entities;
using GdaxHoarder.Data.EntityTypes;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GdaxHoarder
{
    public class TaskExecutor
    {
        private static string BASE_CURRENCY = "USD";
        static TaskExecutor()
        {
            var settings = GdaxSettings.Parse(AppConsts.KEYS_PATH);
            ExchangeClientBase.IsSandbox = settings.IsSandbox;

            _api = new GdaxApi(settings.ToAuthContainer());
        }

        private static GdaxApi _api;
        public static async Task<bool> DepositAch(Burden burden)
        {
            //preRequest(btnBankDeposit);
            var methods = await _api.PaymentMethods.GetPaymentMethodsAsync();

            var ach = methods.Items.FirstOrDefault(a => a.Type == "ach_bank_account");
            if (ach == null)
            {
                taskResult(burden, false, "Please define ACH Bank Deposit in your account before proceeding");
            }

            var resp = await _api.Deposits.PaymentMethod(burden.BurdenTypeAmount, BASE_CURRENCY, ach.Id);
            if (httpSuccess(burden, resp))
            {
                var str = String.Format("Deposit successfuly posted! {0} {1} will be in account by {2}",
                    resp.Amount.ToString("N0"), resp.Currency, resp.PayoutAt);
                taskResult(burden, true, str);
            }

            return resp.HttpResponse.IsSuccessStatusCode;
        }

        public static async Task<bool> BuyCurrency(Burden burden)
        {
            var buyPair = burden.BurdenTypeCurrency + "-" + BASE_CURRENCY;
            var req = new OrdersMarketRequest
            {
                Side = "buy",
                ProductId = buyPair,
                Funds = burden.BurdenTypeAmount.ToString()
            };

            var resp = await _api.Orders.PostOrderMarket(req);
            if (httpSuccess(burden, resp))
            {
                var str = String.Format("Order {0} placed at {1}. Settled: {2}",
                    resp.Id, resp.CreatedAt, resp.Settled);
                taskResult(burden, true, str);
            }

            return resp.HttpResponse.IsSuccessStatusCode;
        }

        public static async Task<bool> WithdrawToWallet(Burden burden)
        {
            var amountToWithdraw = burden.BurdenTypeAmount;
            if (amountToWithdraw <= 0)
            {
                var balance = await _api.Account.ListAccounts();
                var curr = balance.Accounts.FirstOrDefault(a => a.Currency == burden.BurdenTypeCurrency.ToString());
                if (curr == null)
                    return await Task.FromResult(false);

                amountToWithdraw = curr.Available;
            }

            var resp = await _api.Withdrawals.Crypto(
                amountToWithdraw, burden.BurdenTypeCurrency.ToString(), burden.WalletAddr);

            if (httpSuccess(burden, resp))
            {
                var str = String.Format("Withdrawal of {0} {1} requested. Transaction Id: {2}",
                    resp.Amount,
                    resp.Currency,
                    resp.Id);
                taskResult(burden, true, str);
            }

            return resp.HttpResponse.IsSuccessStatusCode;
        }

        public static async Task<ListAccountsResponse> AccountBalances()
        {
            var status = await _api.Account.ListAccounts();
            return status;
        }

        private static void taskResult(Burden burden, bool success, string result)
        {
            var dbLog = new BurdenLog
            {
                BurdenName = burden.ToString(),
                BurdenLogName = result,
                Created = DateTime.Now,
                Success = success
            };
            var table = DbWrapper.Db.GetCollection<BurdenLog>();
            table.Insert(dbLog);

            Debug.WriteLine(dbLog.ToString());
        }

        private static bool httpSuccess(Burden burden, ExchangeResponseGenericBase resp)
        {
            var httpResp = resp.HttpResponse;
            if (!httpResp.IsSuccessStatusCode)
            {
                taskResult(burden, false, httpResp.ErrorMessage());
            }

            return httpResp.IsSuccessStatusCode;
        }
    }
}
