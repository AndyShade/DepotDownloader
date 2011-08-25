﻿/*
 * This file is subject to the terms and conditions defined in
 * file 'license.txt', which is part of this source code package.
 */



using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;

namespace SteamKit2
{
    /// <summary>
    /// This handler handles all user log on/log off related actions and callbacks.
    /// </summary>
    public sealed partial class SteamUser : ClientMsgHandler
    {


        LogOnDetails logonDetails;


        internal SteamUser()
        {
        }


        /// <summary>
        /// Represents the details required to log into Steam3.
        /// </summary>
        public class LogOnDetails
        {
            /// <summary>
            /// Gets or sets the username.
            /// </summary>
            /// <value>The username.</value>
            public string Username { get; set; }
            /// <summary>
            /// Gets or sets the password.
            /// </summary>
            /// <value>The password.</value>
            public string Password { get; set; }

            /// <summary>
            /// Gets or sets the SteamID used for logging into Steam3.
            /// </summary>
            /// <value>
            /// The SteamID.
            /// </value>
            public SteamID SteamID { get; set; }

            /// <summary>
            /// Gets or sets the client Ticket Granting Ticket.
            /// </summary>
            /// <value>The client TGT.</value>
            public ClientTGT ClientTGT { get; set; }
            /// <summary>
            /// Gets or sets the server Ticket Granting Ticket.
            /// </summary>
            /// <value>The server TGT.</value>
            public byte[] ServerTGT { get; set; }
            /// <summary>
            /// Gets or sets the account record.
            /// </summary>
            /// <value>The account record.</value>
            public AuthBlob AccRecord { get; set; }



            /// <summary>
            /// Gets or sets the Steam Guard auth code used to login. This is the code sent to the user's email.
            /// </summary>
            /// <value>The auth code.</value>
            public string AuthCode { get; set; }

            /// <summary>
            /// Gets or sets the account instance. 1 for the PC instance or 2 for the Console (PS3) instance.
            /// </summary>
            /// <value>The account instance.</value>
            public uint AccountInstance { get; set; }


            public LogOnDetails()
            {
                AccountInstance = 1; // use the default pc steam instance
            }
        }

        /// <summary>
        /// Logs the client into the Steam3 network. The client should already have been connected at this point.
        /// Results are returned in a <see cref="LogOnCallback"/>.
        /// </summary>
        /// <param name="details">The details.</param>
        public void LogOn( LogOnDetails details )
        {
            this.logonDetails = details;

            var logon = new ClientMsgProtobuf<MsgClientLogon>();

            SteamID steamId = new SteamID( 0, details.AccountInstance, Client.ConnectedUniverse, EAccountType.Individual );

            if ( details.ClientTGT != null )
            {
                steamId.SetFromSteam2( details.ClientTGT.UserID, this.Client.ConnectedUniverse );
            }
            else if ( details.SteamID != null )
            {
                steamId = details.SteamID;
            }
            else if ( details.Password != null )
            {
                // this condition exists for steam3 logon without any steam2 details
            }
            else
            {
                throw new ArgumentException( "LogOn requires a SteamID or ClientTGT to be set in the LogOnDetails." );
            }

            steamId.AccountInstance = details.AccountInstance;

            uint localIp = NetHelpers.GetIPAddress( this.Client.GetLocalIP() );

            logon.ProtoHeader.client_session_id = 0;
            logon.ProtoHeader.client_steam_id = steamId.ConvertToUint64();

            logon.Msg.Proto.obfustucated_private_ip = localIp ^ MsgClientLogon.ObfuscationMask;

            logon.Msg.Proto.account_name = details.Username;
            logon.Msg.Proto.password = details.Password;

            logon.Msg.Proto.protocol_version = MsgClientLogon.CurrentProtocol;
            logon.Msg.Proto.client_os_type = 10; // windows
            logon.Msg.Proto.client_language = "english";

            if ( details.AccRecord != null )
            {
                MicroTime creationTime = details.AccRecord.CreationTime;

                logon.Msg.Proto.rtime32_account_creation = creationTime.ToUnixTime();
                logon.Msg.Proto.email_address = details.AccRecord.Email;
            }

            // because steamkit doesn't attempt to find the best cellid
            // we'll just use the default one
            // this is really only relevant for steam2, so it's a mystery as to why steam3 wants to know
            logon.Msg.Proto.cell_id = 0;

            // we're now using the latest steamclient package version, this is required to get a proper sentry file for steam guard
            logon.Msg.Proto.client_package_version = 1634;

            // this is not a proper machine id that Steam accepts
            // but it's good enough for identifying a machine
            logon.Msg.Proto.machine_id = Utils.GenerateMachineID();


            if ( details.ServerTGT != null )
            {
                byte[] serverTgt = new byte[ details.ServerTGT.Length + 4 ];

                Array.Copy( BitConverter.GetBytes( localIp ), serverTgt, 4 );
                Array.Copy( details.ServerTGT, 0, serverTgt, 4, details.ServerTGT.Length );

                logon.Msg.Proto.steam2_auth_ticket = serverTgt;
            }


            // steam guard
            logon.Msg.Proto.auth_code = details.AuthCode;

            string sentryFile = ClientConfig.GetSentryFile( logonDetails.Username );
            byte[] sentryData = null;

            if ( sentryFile != null )
            {
                try
                {
                    sentryData = File.ReadAllBytes( sentryFile );
                }
                catch { }
            }

            if ( sentryData != null )
            {
                logon.Msg.Proto.sha_sentryfile = CryptoHelper.SHAHash( sentryData );
                logon.Msg.Proto.eresult_sentryfile = ( int )EResult.OK;
            }
            else
            {
                logon.Msg.Proto.eresult_sentryfile = ( int )EResult.FileNotFound;
            }

            this.Client.Send( logon );
        }

        public void LogOnAnonUser()
        {
            var logon = new ClientMsgProtobuf<MsgClientLogon>();

            SteamID auId = new SteamID( 00, 0, Client.ConnectedUniverse, EAccountType.AnonUser );

            logon.ProtoHeader.client_session_id = 0;
            logon.ProtoHeader.client_steam_id = auId.ConvertToUint64();

            logon.Msg.Proto.protocol_version = MsgClientLogon.CurrentProtocol;

            this.Client.Send( logon );
        }

        /// <summary>
        /// Logs the client off of the Steam3 network. This method does not disconnect the client.
        /// Results are returned in a <see cref="LogOffCallback"/>.
        /// </summary>
        public void LogOff()
        {
            var logOff = new ClientMsgProtobuf<MsgClientLogOff>();
            this.Client.Send( logOff );
        }

        /// <summary>
        /// Gets the SteamID of this client. This value is assigned after a logon attempt has succeeded.
        /// </summary>
        /// <value>The SteamID.</value>
        public SteamID GetSteamID()
        {
            return this.Client.SteamID;
        }


        /// <summary>
        /// Handles a client message. This should not be called directly.
        /// </summary>
        /// <param name="e">The <see cref="SteamKit2.ClientMsgEventArgs"/> instance containing the event data.</param>
        public override void HandleMsg( ClientMsgEventArgs e )
        {
            switch ( e.EMsg )
            {
                case EMsg.ClientLogOnResponse:
                    HandleLogOnResponse( e );
                    break;

                case EMsg.ClientNewLoginKey:
                    HandleLoginKey( e );
                    break;

                case EMsg.ClientSessionToken:
                    HandleSessionToken( e );
                    break;

                case EMsg.ClientLoggedOff:
                    HandleLoggedOff( e );
                    break;

                case EMsg.ClientUpdateMachineAuth:
                    HandleUpdateMachineAuth( e );
                    break;

                case EMsg.ClientAccountInfo:
                    HandleAccountInfo( e );
                    break;

                case EMsg.ClientWalletInfoUpdate:
                    HandleWalletInfo( e );
                    break;
            }
        }





        #region ClientMsg Handlers
        void HandleLoggedOff( ClientMsgEventArgs e )
        {
            var loggedOff = new ClientMsgProtobuf<MsgClientLoggedOff>();

            try
            {
                loggedOff.SetData( e.Data );
            }
            catch ( Exception ex )
            {
                DebugLog.WriteLine( "SteamUser", "HandleLoggedOff encountered an exception while reading client msg.\n{0}", ex.ToString() );
                return;
            }

#if STATIC_CALLBACKS
            SteamClient.PostCallback( new LoggedOffCallback( Client, loggedOff.Msg.Proto ) );
#else
            this.Client.PostCallback( new LoggedOffCallback( loggedOff.Msg.Proto ) );
#endif
        }
        void HandleUpdateMachineAuth( ClientMsgEventArgs e )
        {
            var machineAuth = new ClientMsgProtobuf<MsgClientUpdateMachineAuth>();

            try
            {
                machineAuth.SetData( e.Data );
            }
            catch ( Exception ex )
            {
                DebugLog.WriteLine( "SteamUser", "HandleUpdateMachineAuth encountered an exception while reading client msg.\n{0}", ex.ToString() );
                return;
            }

            var response = new ClientMsgProtobuf<MsgClientUpdateMachineAuthResponse>();

            try
            {
                using ( FileStream fs = File.Open( machineAuth.Msg.Proto.filename, FileMode.Create, FileAccess.ReadWrite, FileShare.None ) )
                {
                    fs.Write( machineAuth.Msg.Proto.bytes, ( int )machineAuth.Msg.Proto.offset, ( int )machineAuth.Msg.Proto.cubtowrite );
                }

                ClientConfig.AddSentryFile( logonDetails.Username, machineAuth.Msg.Proto.filename );

                response.ProtoHeader.job_id_target = machineAuth.ProtoHeader.job_id_source;

                response.Msg.Proto.eresult = ( uint )EResult.InvalidParam;
                //response.Msg.Proto.filename = machineAuth.Msg.Proto.filename;
                response.Msg.Proto.sha_file = CryptoHelper.SHAHash( machineAuth.Msg.Proto.bytes );
                response.Msg.Proto.offset = machineAuth.Msg.Proto.offset;
                response.Msg.Proto.cubwrote = machineAuth.Msg.Proto.cubtowrite;
            }
            catch
            {
                // note: i'm unsure if this is the proper response
                response.Msg.Proto.eresult = ( uint )EResult.Fail;
            }

            this.Client.Send( response );
        }
        void HandleSessionToken( ClientMsgEventArgs e )
        {
            var sessToken = new ClientMsgProtobuf<MsgClientSessionToken>();

            try
            {
                sessToken.SetData( e.Data );
            }
            catch ( Exception ex )
            {
                DebugLog.WriteLine( "SteamUser", "HandleSessionToken encountered an exception while reading client msg.\n{0}", ex.ToString() );
                return;
            }

#if STATIC_CALLBACKS
            var callback = new SessionTokenCallback( Client, sessToken.Msg.Proto );
            SteamClient.PostCallback( callback );
#else
            var callback = new SessionTokenCallback( sessToken.Msg.Proto );
            this.Client.PostCallback( callback );
#endif
        }
        void HandleLoginKey( ClientMsgEventArgs e )
        {
            var loginKey = new ClientMsg<MsgClientNewLoginKey, ExtendedClientMsgHdr>();

            try
            {
                loginKey.SetData( e.Data );
            }
            catch ( Exception ex )
            {
                DebugLog.WriteLine( "SteamUser", "HandleLoginKey encountered an exception while reading client msg.\n{0}", ex.ToString() );
                return;
            }

            var resp = new ClientMsg<MsgClientNewLoginKeyAccepted, ExtendedClientMsgHdr>();
            resp.Msg.UniqueID = loginKey.Msg.UniqueID;

            this.Client.Send( resp );

#if STATIC_CALLBACKS
            var callback = new LoginKeyCallback( Client, loginKey.Msg );
            SteamClient.PostCallback( callback );
#else
            var callback = new LoginKeyCallback( loginKey.Msg );
            this.Client.PostCallback( callback );
#endif
        }
        void HandleLogOnResponse( ClientMsgEventArgs e )
        {
            if ( e.IsProto )
            {
                var logonResp = new ClientMsgProtobuf<MsgClientLogOnResponse>();

                try
                {
                    logonResp.SetData( e.Data );
                }
                catch ( Exception ex )
                {
                    DebugLog.WriteLine( "SteamUser", "HandleLogOnResponse encountered an exception while reading client msg.\n{0}", ex.ToString() );
                    return;
                }

#if STATIC_CALLBACKS
                var callback = new LogOnCallback( Client, logonResp.Msg.Proto );
                SteamClient.PostCallback( callback );
#else
                var callback = new LogOnCallback( logonResp.Msg.Proto );
                this.Client.PostCallback( callback );
#endif
            }
        }
        void HandleAccountInfo( ClientMsgEventArgs e )
        {
            var accInfo = new ClientMsgProtobuf<MsgClientAccountInfo>( e.Data );

#if STATIC_CALLBACKS
            var callback = new AccountInfoCallback( Client, accInfo.Msg.Proto );
            SteamClient.PostCallback( callback );
#else
            var callback = new AccountInfoCallback( accInfo.Msg.Proto );
            this.Client.PostCallback( callback );
#endif
        }
        void HandleWalletInfo( ClientMsgEventArgs e )
        {
            var walletInfo = new ClientMsgProtobuf<MsgClientWalletInfoUpdate>( e.Data );

#if STATIC_CALLBACKS
            var callback = new WalletInfoCallback( Client, walletInfo.Msg.Proto );
            SteamClient.PostCallback( callback );
#else
            var callback = new WalletInfoCallback( walletInfo.Msg.Proto );
            this.Client.PostCallback( callback );
#endif
        }
        #endregion
    }
}