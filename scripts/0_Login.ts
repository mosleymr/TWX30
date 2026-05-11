systemscript
gosub :SCRIPTCHECK
setvar $VERSION "4.0"
setvar $MODDATE "03-31-2026"
setvar $GAME GAME
uppercase $GAME
setvar $CONFIGGAME GAMENAME&".cfg"
setvar $GAMESTATS GAMENAME&"-"&$GAME&"-Stats.txt"
setvar $LSDSTATS GAMENAME&"-"&$GAME&"-LSDStats.txt"
loadvar $GETMESTATSNOW

gosub :INITSTARTUP
gosub :GLOBALS
seteventtrigger LOSTCONNECTION :LOST "Connection lost"
seteventtrigger LOSTCONNECT :LOST "Disconnecting from server..."
settexttrigger CONX :CON "Please enter your name"
settextlinetrigger TYPE1 :TYPE1 "Server v1."
settextlinetrigger TYPE2 :TYPE2 "TWGS v2."
pause
:TYPE1
setvar $TYPE "v1"
setvar $SERVERTYPE $TYPE
pause
:TYPE2
setvar $TYPE "v2"
setvar $SERVERTYPE $TYPE
pause
:PRIVATE
killalltriggers
settextlinetrigger GOODPW :GOODPW " module now loading."
settexttrigger NOGOODPW :NOGOODPW "Invalid password!"
send $PRIVATEPW&"*"
pause
:NOGOODPW
send "q"
halt
:CON
loadvar $LOGINNAME
if ($LOGINNAME = 0)
  send ""&LOGINNAME&"*"
  setvar $LOGINNAME LOGINNAME

else
  send ""&$LOGINNAME&"*"
end
killtrigger GOODPW
settexttrigger GOODPW :GOODPW " module now loading."


if ($WHOS_ONLINE = "Y")
  send "#"
  waiton "Players Online"
end




if (($WHOS_ONLINE = "Y") and ($HIGHSCORES = "N"))
  if ($STOPATTMENU = "N")
    if (($WANTSTATS = "N") and ($EXITSERVER = "Y"))
      killalltriggers
      settexttrigger GET_OUT1 :GET_USOUT "Enter your choice:"
      setdelaytrigger GET_OUT2 :GET_USOUT 1000
      pause
      :GET_USOUT
      killalltriggers
      disconnect
      setdelaytrigger XITOUT :XITUSOUT 2000
      pause
      :XITUSOUT
      halt
    end
  end
end


settexttrigger PRIVATE :PRIVATE "This is a private game.  Please enter a password:"
setdelaytrigger NOGAMEHERE :NOGAMEHERE 20000
send GAME
pause
pause
:GOODPW

setdelaytrigger PPX :PPX 800
pause
:PPX

send "*"
waiton "Enter your choice:"
killtrigger GOODPW
killtrigger NOGOODPW
killtrigger NOGAMEHERE
killtrigger PRIVATE
if ($LPORTOVERRIDE <> "Y")
  loadvar $LISTENPORT
  getmenuvalue "TWX_LISTENPORT" $VAL
  if ($LISTENPORT < 1)
    getmenuvalue "TWX_LISTENPORT" $VAL
    setvar $LISTENPORT $VAL

    goto :LPORT
  end
  if ($LISTENPORT = $VAL)
  else
    killalltriggers
    sound "FAILED"
    disconnect
    echo ANSI_10 "*TWX_LISTENPORT Should be : "&ANSI_14 $LISTENPORT "*"
    halt
  end
end
:LPORT

fileexists $YNFILE $GAMESTATS
if (($WANTSTATS = "N") and ($EXITSERVER = "Y"))
else
  if (($GETMESTATSNOW = 1) or ($WANTSTATS = "Y") or ($YNFILE <> 1))
    gosub :GETMESTATSNOW
  end
  loadvar $CONFIGFOUND
  if ($CONFIGFOUND)
  else
    :GETMESTATSNOW
    delete $GAMESTATS
    gosub :_GAMESTATS~GAMESTATS
    if ($LPORTOVERRIDE <> "Y")
      savevar $LISTENPORT
    end
    savevar $LOGINNAME
    setvar $MAX_CORPIES $_GAMESTATS~MAX_CORPIES
    setvar $STEAL_FACTOR $_GAMESTATS~STEAL_FACTOR
    setvar $STEAL_DIVISOR $_GAMESTATS~STEAL_DIVISOR
    setvar $ROB_FACTOR $_GAMESTATS~ROB_FACTOR
    setvar $ROB_MULTIPLIER $_GAMESTATS~ROB_MULTIPLIER
    setvar $PTRADESETTING $_GAMESTATS~PTRADESETTING
    setvar $_CK_PTRADESETTING $_GAMESTATS~_CK_PTRADESETTING
    setvar $PORT_MAX $_GAMESTATS~PORT_MAX
    setvar $PRODUCTIONRATE $_GAMESTATS~PRODUCTIONRATE
    setvar $MAXPRODREGEN $_GAMESTATS~MAXPRODREGEN
    setvar $MBBS $_GAMESTATS~MBBS
    setvar $MEGABUG $_GAMESTATS~MEGABUG
    setvar $BUSTCLEAR $_GAMESTATS~BUSTCLEAR
    setvar $MAKEBWARP $_GAMESTATS~MAKEBWARP
    setvar $UPBWARP $_GAMESTATS~UPBWARP
    savevar $STEAL_FACTOR
    savevar $STEAL_DIVISOR
    savevar $ROB_FACTOR
    savevar $ROB_MULTIPLIER
    savevar $PTRADESETTING
    savevar $_CK_PTRADESETTING
    savevar $MAX_CORPIES
    savevar $PORT_MAX
    savevar $PRODUCTIONRATE
    savevar $MAXPRODREGEN
    savevar $MBBS
    savevar $MEGABUG
    savevar $BUSTCLEAR
    savevar $MAKEBWARP
    savevar $UPBWARP
    setvar $STATSRUN $_GAMESTATS~STATSRUN
    savevar $STATSRUN
    savevar $SERVERTYPE
    setvar $CONFIGFOUND TRUE
    savevar $CONFIGFOUND
    setvar $CKOPTOUTCNSETTINGS "Y"
    setvar $CKSURROUNDFIGTYPE "D"
    setvar $CKSURROUNDFIGAMOUNT 1
    setvar $CKSURROUNDARMIDAMOUNT 0
    setvar $CKSURROUNDLIMPETAMOUNT 0
    setvar $CKSHIPCAPAUTOLOAD "nowhere"
    setvar $BOT_TURN_LIMIT 15
    setvar $USER_COMMAND_LINE "0 0 0 0 0 0 0 0 0 0 0 0 0"
    savevar $CKOPTOUTCNSETTINGS
    savevar $CKSURROUNDFIGTYPE
    savevar $CKSURROUNDFIGAMOUNT
    savevar $CKSURROUNDARMIDAMOUNT
    savevar $CKSURROUNDLIMPETAMOUNT
    savevar $CKSHIPCAPAUTOLOAD
    savevar $BOT_TURN_LIMIT
    savevar $USER_COMMAND_LINE
    gosub :LSD_STATS
    if ($GETMESTATSNOW = 1)
      setvar $GETMESTATSNOW 0
      return
    end
  end
end
loadvar $BOT_NAME
loadvar $STATSRUN
loadvar $ACCESSMODE
:MENULOOP
if ($ACCESSMODE = 1)
  setvar $ACCESSMODE 0
  fileexists $FILEEXIST "scripts\_timerEntry.ts"
  fileexists $FILEEXIST2 "scripts\_timerEntry.cts"
  if (($FILEEXIST = TRUE) or ($FILEEXIST2 = TRUE))
    load "_timerEntry"
  end
  halt
end

echo "**"&ANSI_12 "    ®®®" ANSI_10 "µ" ANSI_10 " Team Kraaken " ANSI_9 " Login " $VERSION " " ANSI_10 "Æ" ANSI_12 "¯¯¯"
echo "*"&ANSI_7 "       Made By :"&ANSI_14&" Vid Kid/CareTaker*"
echo "*"&ANSI_10 "       Last Modified : " ANSI_13 $MODDATE
echo "*"&ANSI_10 "       Starting Point: " ANSI_13 "OffLine**"
















setvar $LOGFILE1 "scripts\CN_BackGround.cts"
fileexists $TESTCN $LOGFILE1
if ($TESTCN = TRUE)
  gosub :SCRIPTCHECK
  if ($CN_BACKGROUND_RUNNING = 0)
    load "CN_BACKGROUND"
  end
end

setvar $LOGONFILE1 "scripts/_vid_fighits.cts"
fileexists $TEST1 $LOGONFILE1
if ($TEST1 = TRUE)
  gosub :SCRIPTCHECK
  if ($FIGHITSRUNNING = 0)
    load "_VID_FIGHITS"
  end
end
if ($HIGHSCORES = "Y")
  echo "*"
  send "h**"
end
if ($EXITSERVER = "Y")
  killalltriggers
  send "*"
  settexttrigger ENDOUT :XITOUT "Enter your choice:"
  setdelaytrigger XITOUT2 :XITOUT 10000
  pause
  :XITOUT
  killalltriggers
  disconnect
  setdelaytrigger XOUT :XOUT 2000
  pause
  :XOUT
  halt
end
if ($STOPATTMENU = "Y")
  echo "*"
  send "h**"
  :DOUBLEBACK
  killalltriggers
  echo #27&"[1A"&#27&"[K*"
  send #145
  echo #27&"[19C"
  setdelaytrigger DOUBLEBACK :DOUBLEBACK 8000
  settextlinetrigger DOWNTIME :DOWNTIME "elp)"
  seteventtrigger LOSTCONNECTION :LOST "Connection lost"
  pause
end

setvar $LOGONFILE5 "scripts/K_BackGround.cts"
fileexists $TEST5 $LOGONFILE5
if ($TEST5 = TRUE)
  gosub :SCRIPTCHECK
  if ($ONLINE_TEST = 0)
    load "K_BACKGROUND"
  end
end
setvar $LOGONFILE10 "scripts/_vid_mom_overdrive.cts"
fileexists $TEST10 $LOGONFILE10
if ($TEST10 = TRUE)
  gosub :SCRIPTCHECK
  if ($ONLINE_TEST10 = 0)
    load "_VID_MOM_OVERDRIVE"
  end
end
:TOPPER

killalltriggers
setvar $PLACE1 ":Top Triggers"
settexttrigger FIGPRMT :FIGPRMT "You have to destroy"
settexttrigger MINES :MINES "Mined Sector:"

settexttrigger COMMANDPRMT :COMMANDPRMT "] (?=Help)? "
settexttrigger ONPLANET :ONPLANET "Planet command (?=help)"
settexttrigger NAVPOINT :NAVPOINT "NavPoint Settings (?=Help) [Q] :"
settextlinetrigger COPY :COPY "This copy of TW2002 "
settextlinetrigger INIT :INIT "Initializing..."
seteventtrigger CANCELED :LOST "Connect cancelled"
seteventtrigger LOST :LOST "Connection lost"
settexttrigger ONLYONE :ONLYONE "Only one connection is allowed"
settextlinetrigger FULL :FULL "I'm sorry but the game is full."
settextlinetrigger BANNED1 :BANNED1 "Access denied!"
settexttrigger CLOSEDGAME :NOGO "this is a closed game."
settexttrigger CONX :CONX "Please enter your name"
settexttrigger DUPENAME :DUPENAME "Sorry, you cannot use the name"
settextlinetrigger TWPASSWORD :TWPASSWORD "TradeWars Passport"
settextlinetrigger NEWPLAYERENTERS :OPENGAME "You were not found in the player database."
settextlinetrigger NEWPLAYERENTERSPW :OPENGAMEPW "Please enter a password for this game account."
settexttrigger FIRSTNOPASSWORDPLAYERA :FIRSTTIMEINPAUSED "Great! You're on your way to becoming a Galactic Power!"
settexttrigger GETPASSWORD :GETFIRSTPASSWORD "A password is required"
settextlinetrigger REPEATW :REPEATPW "Repeat password"
settexttrigger BANDED2 :BANDED1 "Invalid password."
settextlinetrigger NEWPLAYERA :NEWPLAYERA "would you rather use your BBS name of "
settexttrigger NEWSHIP :SHIP "What do you want to name your ship?"
settexttrigger NEWPLANET :NEWPLANET "What do you want to name your home planet?"
settexttrigger ENTRYPOINT :ENTRYPOINT "Show today's log?"
settextlinetrigger CHECKMESSAGES :CHECKMESSAGES "Searching for messages received since your last time on:"
settextlinetrigger NOMESSAGES :NOMESSAGES "No messages received."



settextlinetrigger NAVPROMPT1 :NAVPROMPT1 "No Sectors are currently being avoided."
settexttrigger AVOIDCLEAR :CLRAVOIDS "Do you wish to clear some avoids? (Y/N) [N]"

setdelaytrigger HOLDIN :LOST 1999850
if ($TWPASS <> 1)
  send "t*"
end
pause
:COPY
setdelaytrigger COPY2 :COPY2 1000
pause
:COPY2
send "*"
pause
:TWPASSWORD
killalltriggers
waiton "TW Passport>"
setvar $TWPASS 1
send "v"
waiton "What is your TW Passport ID (TWPID)?"
send $TWPASSPORT&"*"
sound "CASH_REGISTER"
waiton "Your TW Passport validation has been accepted!"
waiton "Please enter a password for this game account."
setvar $PLACE23 ":TWPassword"


send PASSWORD&"*"
goto :TOPPER
pause
:ENTRYPOINT
if ($READDAILIES = "Y")
  send "y"
  waiton "Include time/date stamp? (Y/N)"
  if ($ACCESSMODE = 1)
    settexttrigger TMENU :TMENU2 "Enter your choice:"
  end
  settexttrigger ENTRYPOINT_LOOP :ENTRYPOINT_LOOP "[Pause]"
  send "y"
  setvar $PLACE ":EntryPoint ReadDailies = Y"

  pause
  :ENTRYPOINT_LOOP
  killtrigger ENTRYPOINT_LOOP
  killtrigger ENDLOOP
  settexttrigger ENTRYPOINT_LOOP :ENTRYPOINT_LOOP "[Pause]"
  setdelaytrigger ENDLOOP :INIT 5500
  send "*"
  pause
elseif ($READDAILIES = "N")
  setvar $PLACE ":EntryPoint ReadDailies = N"

  send "*"
  pause
end
killtrigger ENTRYPOINT_LOOP
killtrigger ENDLOOP
pause
:INIT

killtrigger ENTRYPOINT_LOOP
killtrigger ENDLOOP
pause
:TMENU2
setvar $ACCESSMODE 1
goto :MENULOOP
:DOWNTIME

listactivescripts $SCRIPTS
setvar $A 1
while ($A <= $SCRIPTS)
  lowercase $SCRIPTS[$A]
  getwordpos $SCRIPTS[$A] $LOGIN "_login"
  if ($LOGIN > 0)
    stop $SCRIPTS[$A]
  end
  add $A 1
end
halt
:DUPENAME
setvar $PLACE3 ":DupeName"


getrnd $RND 0 99
send ""
if (($ALIAS <> "") and ($ALLOWALIAS = "Y"))
  send $RND&$ALIAS "*"
else
  send $RND&$NAMEWORD "*"
end
waiton "is what you want? (Y/N)"
send "y"
pause
:FULL
sound "FAILED"
disconnect
halt
:BANDED1
killalltriggers
setvar $PLACE28 ":banded"


sound "FAILED"
echo "*"&ANSI_10 " I've Been Banned Again !*" ANSI_0
disconnect
halt
:NOGO
killalltriggers
setvar $PLACE28 ":noGo"


sound "FAILED"
echo "*"&ANSI_10 " It's a Closed Game !*" ANSI_0
delete $CONFIGGAME
disconnect
halt
:LOST
killalltriggers
echo "*" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "All Done" ANSI_11 " |===" ANSI_3 "--*"

disconnect
halt
:NOGAMEHERE
killalltriggers
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_10 "Game "&ANSI_14 $GAME ANSI_10&" Not Available Anymore." ANSI_11 " |===" ANSI_3 "--*"
delete "data\"&$GAMENAME&".cfg"
sound "FAILED"
disconnect
halt
:ONLYONE
killalltriggers
setvar $PLACE36 ":onlyone"


echo "*"&ANSI_12 "ONLY ONE CONNECTION ALLOWED ON THIS SERVER for a Single IP address.*" ANSI_0

halt
:NAVPROMPT1

setvar $PLACE20 ":NavPrompt1"



send "/"
pause
:NAVPOINT
setvar $PLACE2 ":NavPoint"
send "*"
pause
:SEECOMMANDLINE
send "/"
pause
:MINES
setvar $PLACE420 ":mines"


send "*"
pause
:CLRAVOIDS
setvar $PLACE22 "clravoids"

if ($CLEARVOIDS = "Y")
  setvar $PLACE3 ":clearvoids Y"
  send "yy"
  pause
elseif ($CLEARVOIDS = "N")
  setvar $PLACE3 ":clearvoids N"
  setdelaytrigger SEECOMMANDLINE :SEECOMMANDLINE 800
  send "*"
  pause
end
send "*"



pause
:GETFIRSTPASSWORD

setvar $PLACE8 ":getfirstpassword"

killtrigger ENTRYPOINT_LOOP
killtrigger ENDLOOP
settextlinetrigger WHOSPLAYING :WHOSPLAYING "Who's Playing"
settexttrigger PAUSESPACE :PAUSESPACE " are on the move!"
send PASSWORD "*"


pause
:WHOSPLAYING
setvar $PLACE4 ":WhosPlaying"
setdelaytrigger WHO :WHO 1500
pause
:WHO
setvar $PLACE4 ":Who"
send "*"
pause
:PAUSESPACE

setvar $PLACE5 ":PauseSpace"
killtrigger WHOSPLAYING
killtrigger WHO
send "*"
pause
:REPEATPW
setvar $PLACE7 ":repeatPW"


send PASSWORD "*"
setvar $NEW 1


pause
:NOMESSAGES

setvar $PLACE6 ":NoMessages"
killtrigger CHECKMESSAGES
setvar $NOMESSAGES TRUE
setvar $NOLOGZ TRUE


send "*"
killtrigger COPY
killtrigger INIT
killtrigger ONLYONE
killtrigger FULL
killtrigger BANNED1
killtrigger BANNED2
killtrigger CLOSEDGAME
killtrigger DUPENAME
killtrigger TWPASSWORD
killtrigger NEWPLAYERENTERS
killtrigger NEWPLAYERENTERSPW
killtrigger FIRSTNOPASSWORDPLAYERA
killtrigger NEWPLAYERA
killtrigger NEWPLANET
pause
:CHECKMESSAGES

killtrigger COPY
killtrigger INIT
killtrigger ONLYONE
killtrigger FULL
killtrigger BANNED1
killtrigger BANNED2
killtrigger CLOSEDGAME
killtrigger DUPENAME
killtrigger TWPASSWORD
killtrigger NEWPLAYERENTERS
killtrigger NEWPLAYERENTERSPW
killtrigger FIRSTNOPASSWORDPLAYERA
killtrigger NEWPLAYERA
killtrigger NEWPLANET



settextlinetrigger CHECKMESSAGES2 :CHECKMESSAGES2 "Received from "
settexttrigger DLTPAUSE :DLTPAUSE "[Pause] - Delete messages? (Y/N) [N]"
pause
:CHECKMESSAGES2


killtrigger DLTPAUSE
killtrigger HOLDIN
setvar $PLACE9 ":checkmessages2"




settexttrigger MESSAGEDELAY :MESSAGEDELAY "[Pause] - [Press "
settexttrigger DLTPAUSE :DLTPAUSE "[Pause] - Delete messages? (Y/N) [N]"
if ($READLOGS = "N")
  setvar $PLACE50 "ReadLogs No"



  settextlinetrigger MOREKMESSAGES :MOREKMESSAGES "Received from "
  pause
elseif ($READLOGS = "Y")
  setvar $PLACE151 "ReadLogs = Y"


  setdelaytrigger HOLDIN :LOST 599850
  :READIN_DELAY


  setdelaytrigger PAUSEWHILEREADIN_DELAY :PAUSEWHILEREADIN_DELAY 1400

  pause
end
pause
:PAUSEWHILEREADIN_DELAY

send "*"
goto :READIN_DELAY


setvar $PLACE51 "Pause4Delete"

killtrigger MESSAGEDELAY
killtrigger MOREKMESSAGES
killtrigger MOREMESSAGEDELAY
send "y"
setvar $DONEDELETED 1
pause
:MOREKMESSAGES
:MESSAGEDELAY
killtrigger MESSAGEDELAY
killtrigger MOREKMESSAGES
killtrigger MOREMESSAGEDELAY
if ($READLOGS = "N")
  setvar $PLACE52 ":MessageDelay"

  send "a"
end
pause
:DLTPAUSE

killtrigger MESSAGEDELAY
killtrigger MOREKMESSAGES
killtrigger MOREMESSAGEDELAY
killtrigger READIN_DELAY
if ($CLEARMESSAGES = "Y")
  send "y"
elseif ($CLEARMESSAGES = "N")
  send "*"
end
setvar $PLACE7 ":Done with Messages"
pause
:OPENGAME

setvar $PLACE6 ":openGame"


loadvar $BEENINBEFORE
if (($WANTAUTOSTART = "Y") and ($BEENINBEFORE = 0))
elseif ($BEENINBEFORE = 1)
  killalltriggers
  disconnect
  sound "FAILED"
  halt
else
  echo #27&"[1A"&#27&"[K*"
  echo "*"&ANSI_14 "Do You WANT to play ?"
  echo "*"&ANSI_15 "   Y " ANSI_11 "- " ANSI_10 "Yes " ANSI_15 " N " ANSI_11 "- " ANSI_10 "No   "
  echo "*"
  getconsoleinput $YNASKING SINGLEKEY
  uppercase $YNASKING


  if ($YNASKING <> "Y")
    killalltriggers
    delete $CONFIGGAME
    disconnect
    echo ""&#27&"[7A"&#27&"[1K"&#27&"[2K"&"*"&#27&"[2K"
    echo " " ANSI_3 " --" ANSI_11 "===| " ANSI_15 "All Done" ANSI_11 " |===" ANSI_3 "--*                "
    sound "ding.wav"
    halt
  end
end
killtrigger TMENU
setvar $ACCESSMODE 0
savevar $ACCESSMODE
setvar $BEENINBEFORE 1
savevar $BEENINBEFORE
setvar $NEW 1
send "y"
pause
:OPENGAMEPW
setvar $PLACE16 ":openGamePW"

send PASSWORD "*"
pause
:FIRSTTIMEINPAUSED
setvar $PLACE17 ":FirstTimeInPaused"
if ($NEW = 1)
  send "*"
end
pause
:NEWPLAYERA
setvar $PLACE11 ":newplayerA"


setvar $NEW 1
gettext CURRENTLINE $NAMEWORD "use your BBS name of " "?"
waiton "Use (N)ew Name or (B)BS Name [B] ?"
send "n"
waiton "What Alias do you want to use?"
if (($ALIAS <> "") and ($ALLOWALIAS = "Y"))
  send $ALIAS "*"
else
  send $NAMEWORD "*"
end
waiton "is what you want? (Y/N)"
send "y"
setvar $PLACE12 "NameDone"
pause
:SHIP

setvar $PLACE18 ":Ship"
send $SHIPNAME&"*"
waiton "is what you want?"
getwordpos CURRENTLINE $SHIPNAMEPOS " is what you want?"
cuttext CURRENTLINE $SKIPNAMED 1 ($SHIPNAMEPOS - 1)
if ($SKIPNAMED = $SHIPNAME)
  send "y*"
else
  send "n*"
  goto :SHIP
end
pause
:NEWPLANET

killalltriggers
setvar $PLACE29 ":newplanet"


send $PLANETNAME&"*q"
waiton "Blasting off from"
waiton "Warps to Sector(s) :"
waiton "] (?="
getword CURRENTLINE $WHEREAT 1
killalltriggers
goto :FINISHEDENTRY
:COMMANDPRMT

setvar $PLACE24 ":commandprmt"


setvar $ACCESSMODE 0
savevar $ACCESSMODE
if ((CURRENTSECTOR = 1) and ($NEW <> 1))
  goto :FINISHEDENTRY
end
if (CURRENTSECTOR = STARDOCK)
  if ($DOCKFAST = "Y")
    send "p s gyg qh 'ONLINE!*"
    setvar $PSF 1
    goto :FINISHEDENTRY
  end
end
if ((CURRENTSECTOR <> 1) and (CURRENTSECTOR <> STARDOCK))
  if (PORT.EXISTS[CURRENTSECTOR] and ($PORTFAST = "Y"))



    send "p"
    send "*"
    sound "DING_DONG.WAV"
    waiton "] (?=Help)? :"
  end
  setvar $PLACE25 "commandprmt1"
end


setvar $PLACE19 ":Somewhere in command"
killalltriggers
send " * "


waiton "?"
getword CURRENTLINE $WHEREAT 1
killalltriggers
settexttrigger COMMS_ON1 :COMMS_ON1 "Displaying all messages."
settexttrigger COMMS_OFF1 :COMMS_OFF1 "Silencing all messages."
send "|"
pause
:COMMS_OFF1
killtrigger COMMS_ON1
send "|"
:COMMS_ON1
killtrigger COMMS_OFF1
setvar $PLACE99 ":  Goto :DonePlusWait"
goto :DONEPLUSWAIT
:FIGPRMT

killalltriggers
setvar $PLACE33 ":figprmt"


setvar $ACCESSMODE 0
savevar $ACCESSMODE
send " a z 99887766 y z n p a z 99887766 y * z n a z 99887766 * * /"
waiton "hip"
send " f z 1 * z c d * /"
waiton "hip"
send #145
waiton #145&#8
getword CURRENTLINE $WHEREAT 1
if (PORT.EXISTS[CURRENTSECTOR] and ($PORTFAST = "Y"))



  send "p "
  send "*"
end
sound "ALERT"
goto :FINISHEDENTRY
:ONPLANET

killtrigger HOLDIN
setvar $PLACE32 ":onplanet"


setvar $ACCESSMODE 0
savevar $ACCESSMODE
send " c c q s* @"
waiton "Average Interval Lag"
waiton "elp"
getword CURRENTLINE $WHEREAT 1
goto :DONE
:FINISHEDENTRY

setvar $PLACE40 ":FinishedEntry"


setvar $BEENINBEFORE 1
savevar $BEENINBEFORE
if (($NEW <> 1) and ((CURRENTSECTOR = 1) and ($LANDONTERRA = "Y")))
  setvar $ONTERRA 1
  settexttrigger SCANNER :SCANNER "Land on which planet"
  settexttrigger ONTERRA :ONTERRA "(T)ake Colonists? [T] (Q to leave)"
  send "l "
  pause
  :SCANNER
  killtrigger ONTERRA
  send "1* "
  :ONTERRA
  killtrigger SCANNER
  send "'ONLINE , Landed on Terra!*"
  waiton "ub-space c"
  gosub :TERRAKIT~TERRA_KITS
  setvar $KILLKIT $TERRAKIT~KILLKIT
  if ($KILLKIT = 1)
    setvar $ONTERRA 0
    send "*"
    waiton "elp"
    getword CURRENTLINE $WHEREAT 1
    goto :DONE
  end
  goto :DONEPLUSWAIT
end
if ($PSF = 1)
  sound "DING_DONG.WAV"
  halt
end
if (($NEW <> 1) and ((CURRENTSECTOR = 1) and ($LANDONTERRA = "N")))
  send "*"
  waiton "elp"
  getword CURRENTLINE $WHEREAT 1
  goto :DONE
end
if ($NEW = 1)
  stop "_vid_startup"
  if ($SSCOMM <> 0)
    send "c n 4 "&$SSCOMM&" * *q* "
    waiton "<Computer deactivated>"
    waiton "] ("
  end
  gosub :_VID_STARTUP~TOP
  if (($BOT_NAME = 0) and ($MAKEORJOIN <> "N"))
    gosub :_VID_PREGAME~STARTUP
  end
  goto :DONE
end
:DONE


setvar $PLACE98 ":Done"
send #145
waiton #145&#8
getword CURRENTLINE $WHEREAT 1
if ($WHEREAT = "Planet")
  send "c/s*#"
end
if ($WHEREAT = "Command")
end

send #145
waiton #145&#8
getword CURRENTLINE $WHEREAT 1
if ($WHEREAT = "Citadel")
  if ($CLEARMESSAGES = "Y")



    send ":y"
  end











  send "'ONLINE!*"
  waiton "ub-space c"
  fileexists $YN "scripts/"&$BOTUSING
  if ($ONLINEBOT = "Y")
    if ($YN = TRUE)
      load $BOTUSING
      setdelaytrigger BOTGOING2 :BOTGOING2 5500
      pause
    end
  end
  :BOTGOING2


  killtrigger BOTGOING2
  sound "DING"
  halt
end

gosub :SCRIPTCHECK
setvar $LOGONFILE2 "scripts/_ph_chargera.cts"
fileexists $TEST2 $LOGONFILE2
if ($TEST2)
  if ($_PH_CHARGERA = 0)
    send "/"
    waiton "hip "
    waiton "] (?=Help)?"
    getword CURRENTLINE $WHEREAT 1
    load "_PH_CHARGERA.CTS"
  end
  setvar $CHARGER 1
end
setvar $LOGONFILE3 "scripts/_ph_charger.cts"
fileexists $TEST3 $LOGONFILE3
if ($TEST3)
  if (($_PH_CHARGER = 0) and ($CHARGER <> 1))
    load "_PH_CHARGER.CTS"
  end
end
if ($ONTERRA <> 1)
  killalltriggers
  send "ctq* /"
  waiton "hip"
  waiton "elp"
  getword CURRENTLINE $WHEREAT 1
  setvar $LOGONFILE4 "scripts\_ck_equip_haggle_tracker.cts"
  fileexists $TEST4 $LOGONFILE4
  striptext $LOGONFILE4 "scripts\"
  if ($TEST4)
    if (($HAGGLETRACKER = 0) and (($WHEREAT = "Citadel") or ($WHEREAT = "Command")))
      load "_CK_EQUIP_HAGGLE_TRACKER"
      waiton "Credits        :"
      waiton "elp"
    end
  end
  if ($CLEARMESSAGES = "Y")



    send ":y"
  end

  killalltriggers
  settexttrigger COMMS_ON3 :COMMS_ON3 "Displaying all messages."
  settexttrigger COMMS_OFF3 :COMMS_OFF3 "Silencing all messages."
  send "|"
  pause
  :COMMS_OFF3
  killtrigger COMMS_ON3
  send "|"
  :COMMS_ON3
  killtrigger COMMS_OFF3
  send "'ONLINE!*"
  waiton "ub-space c"
end
fileexists $YN "scripts/"&$BOTUSING
if ($ONLINEBOT = "Y")
  if ($YN = TRUE)
    load $BOTUSING
    setdelaytrigger BOTGOING1 :BOTGOING1 5500
    pause
  end
end
:BOTGOING1
sound "DING"
setvar $BEENINBEFORE 1
savevar $BEENINBEFORE
halt
:DONEPLUSWAIT
setvar $PLACE97 ":DonePlusWait"
setdelaytrigger DONEPLUSWAIT :FINISHEDENTRY 2000
setvar $ONCETHRU 1
pause
halt
:SCRIPTCHECK
listactivescripts $SCRIPTS
listactivescripts $SCRIPTSX
:DUPLICATES
setvar $A 1
:DUPLICATES0
while ($A <= $SCRIPTS)
  setvar $B 1
  lowercase $SCRIPTS[$A]
  lowercase $SCRIPTSX[$A]
  getwordpos $SCRIPTS[$A] $MOM "mom_b"
  if ($MOM > 0)
    stop $SCRIPTS[$A]
  end
  if ($SCRIPTS[$A] = "_vid_fighits.cts")
    setvar $FIGHITSRUNNING 1
  end
  if ($SCRIPTS[$A] = "_ph_chargera.cts")
    setvar $_PH_CHARGERA 1
  end
  if ($SCRIPTS[$A] = "_ph_charger.cts")
    setvar $_PH_CHARGER 1
  end
  if ($SCRIPTS[$A] = "_ck_equip_haggle_tracker.cts")
    setvar $HAGGLETRACKER 1
  end
  if ($SCRIPTS[$A] = "k_background.cts")
    setvar $ONLINE_TEST 1
  end
  if ($SCRIPTS[$A] = "_vid_mom_overdrive.cts")
    setvar $ONLINE_TEST10 1
  end
  if ($SCRIPTS[$A] = "cn_background.cts")
    setvar $CN_BACKGROUND_RUNNING 1
  end
  while ($B <= $SCRIPTS)
    lowercase $SCRIPTSX[$B]
    if (($A <> $B) and ($SCRIPTS[$A] = $SCRIPTSX[$B]))
      stop $SCRIPTS[$A]
      goto :SCRIPTCHECK
    end
    add $B 1
  end
  add $A 1
end
return
:INITSTARTUP
if (PASSWORD = "")
  echo "**"&ANSI_12&"Fill in your TWX : "&ANSI_11&"PassWord"&ANSI_12&" before continuing!*"
  sound "FAILED"
  disconnect
  halt
end
if (LOGINNAME = "")
  echo "**"&ANSI_12&"Fill in your TWX : "&ANSI_11&"Login Name"&ANSI_12&" before continuing!*"
  sound "FAILED"
  disconnect
  halt
end
if ((GAME = " ") or (GAME = ""))
  echo "**"&ANSI_12&"Fill in your TWX : "&ANSI_11&"GameLetter"&ANSI_12&" before continuing!*"
  :LOST
  sound "FAILED"
  disconnect
  halt
end
return
:GLOBALS
setvar $GLOBALS "Globals.cfg"
setvar $TEMPCONFIG GAMENAME&"_"&$GLOBALS
fileexists $EXISTS $TEMPCONFIG
if ($EXISTS = 1)
  setvar $_VID_PREGAME~GLOBALS $TEMPCONFIG
  readtoarray $TEMPCONFIG $LINECOUNT

  goto :CLEANUP
end
fileexists $CHECK $GLOBALS
if (($CHECK = 1) and ($EXISTS <> 1))
  setvar $_VID_PREGAME~GLOBALS $GLOBALS
  readtoarray $GLOBALS $LINECOUNT

  goto :CLEANUP
elseif ($CHECK <> 1)
  killalltriggers
  disconnect
  gosub :MAKEGLOBLES~MAKEFILE
  echo "**"&ANSI_11&"You need to "&ANSI_14&"Edit the FILE "&ANSI_11&": "&ANSI_14 $GLOBALS&"*"
  echo "*"&ANSI_11&"In your TWX Root Directory*"
  halt
end
halt
:CLEANUP
setvar $PORTFAST $LINECOUNT[1]
striptext $PORTFAST "Port Fast: "
uppercase $PORTFAST
setvar $DOCKFAST $LINECOUNT[2]
striptext $DOCKFAST "Dock Fast: "
uppercase $DOCKFAST
setvar $LANDONTERRA $LINECOUNT[3]
striptext $LANDONTERRA "Land On Terra: "
uppercase $LANDONTERRA
setvar $READDAILIES $LINECOUNT[4]
striptext $READDAILIES "Read Dailies: "
uppercase $READDAILIES
setvar $READLOGS $LINECOUNT[5]
striptext $READLOGS "Read Todays Log: "
uppercase $READLOGS
setvar $CLEARMESSAGES $LINECOUNT[6]
striptext $CLEARMESSAGES "Clear Messages: "
uppercase $CLEARMESSAGES
setvar $CLEARVOIDS $LINECOUNT[7]
striptext $CLEARVOIDS "Clear Avoids: "
uppercase $CLEARVOIDS
setvar $ALLOWALIAS $LINECOUNT[8]
striptext $ALLOWALIAS "UseAlias: "
uppercase $ALLOWALIAS
setvar $ALIAS $LINECOUNT[9]
striptext $ALIAS "Alias Name: "
setvar $SSCOMM $LINECOUNT[10]
striptext $SSCOMM "SSComms: "
setvar $SHIPNAME $LINECOUNT[11]
striptext $SHIPNAME "ShipName: "
setvar $PLANETNAME $LINECOUNT[12]
striptext $PLANETNAME "PlanetName: "
setvar $MAKEORJOIN $LINECOUNT[13]
striptext $MAKEORJOIN "Make or Join Corp: "
uppercase $MAKEORJOIN
setvar $WHOS_ONLINE $LINECOUNT[16]
striptext $WHOS_ONLINE "Show Who's Online: "
uppercase $WHOS_ONLINE
setvar $WANTSTATS $LINECOUNT[17]
striptext $WANTSTATS "GetMeStatsNow: "
uppercase $WANTSTATS
setvar $HIGHSCORES $LINECOUNT[18]
striptext $HIGHSCORES "High Scores: "
uppercase $HIGHSCORES
setvar $STOPATTMENU $LINECOUNT[19]
striptext $STOPATTMENU "Stop @ Tmenu: "
uppercase $STOPATTMENU
setvar $EXITSERVER $LINECOUNT[20]
striptext $EXITSERVER "Exit Server: "
uppercase $EXITSERVER
setvar $WANTAUTOSTART $LINECOUNT[21]
striptext $WANTAUTOSTART "AutoStart: "
uppercase $WANTAUTOSTART
setvar $LPORTOVERRIDE $LINECOUNT[22]
striptext $LPORTOVERRIDE "Listening Port OverRide: "
uppercase $LPORTOVERRIDE
setvar $PRIVATEPW $LINECOUNT[23]
striptext $PRIVATEPW "Private Game Password: "
setvar $TWPASSPORT $LINECOUNT[24]
striptext $TWPASSPORT "TradeWars Passport: "
uppercase $TWPASSPORT
setvar $ONLINEBOT $LINECOUNT[25]
striptext $ONLINEBOT "Start with Bot ONLINE: "
setvar $BOTUSING $LINECOUNT[26]
striptext $BOTUSING "BotUsing: "




return
:LSD_STATS

readtoarray $LSDSTATS $LSD
striptext $LSD[1] "$LSD_LIMPREMOVALCOST="
setvar $LSD_LIMPREMOVALCOST $LSD[1]
savevar $LSD_LIMPREMOVALCOST
striptext $LSD[2] "$LSD_REREGISTERCOST="
setvar $LSD_REREGISTERCOST $LSD[2]
savevar $LSD_REREGISTERCOST
striptext $LSD[3] "$LSD_GENCOST="
setvar $LSD_GENCOST $LSD[3]
savevar $LSD_GENCOST
striptext $LSD[4] "$LSD_ARMIDCOST="
setvar $LSD_ARMIDCOST $LSD[4]
savevar $LSD_ARMIDCOST
striptext $LSD[5] "$LSD_LIMPCOST="
setvar $LSD_LIMPCOST $LSD[5]
savevar $LSD_LIMPCOST
striptext $LSD[6] "$LSD_BEACON="
setvar $LSD_BEACON $LSD[6]
savevar $LSD_BEACON
striptext $LSD[7] "$LSD_TWARPICOST="
setvar $LSD_TWARPICOST $LSD[7]
savevar $LSD_TWARPICOST
striptext $LSD[8] "$LSD_TWARPIICOST="
setvar $LSD_TWARPIICOST $LSD[8]
savevar $LSD_TWARPIICOST
striptext $LSD[9] "$LSD_TWARPUPCOST="
setvar $LSD_TWARPUPCOST $LSD[9]
savevar $LSD_TWARPUPCOST
striptext $LSD[10] "$LSD_PSCAN="
setvar $LSD_PSCAN $LSD[10]
savevar $LSD_PSCAN
striptext $LSD[11] "$LSD_ATOMICCOST="
setvar $LSD_ATOMICCOST $LSD[11]
savevar $LSD_ATOMICCOST
striptext $LSD[12] "$LSD_CORBOCOST="
setvar $LSD_CORBOCOST $LSD[12]
savevar $LSD_CORBOCOST
striptext $LSD[13] "$LSD_EPROBE="
setvar $LSD_EPROBE $LSD[13]
savevar $LSD_EPROBE
striptext $LSD[14] "$LSD_PHOTONCOST="
setvar $LSD_PHOTONCOST $LSD[14]
savevar $LSD_PHOTONCOST
striptext $LSD[15] "$LSD_CLOAKCOST="
setvar $LSD_CLOAKCOST $LSD[15]
savevar $LSD_CLOAKCOST
striptext $LSD[16] "$LSD_DISRUPTCOST="
setvar $LSD_DISRUPTCOST $LSD[16]
savevar $LSD_DISRUPTCOST
striptext $LSD[17] "$LSD_HOLOCOST="
setvar $LSD_HOLOCOST $LSD[17]
savevar $LSD_HOLOCOST
striptext $LSD[18] "$LSD_DSCANCOST="
setvar $LSD_DSCANCOST $LSD[18]
savevar $LSD_DSCANCOST
delete $LSDSTATS
return

# includes:
include "include/_GAMESTATS.ts"
include "include/_VID_LIB.ts"
include "include/_VID_PREGAME.ts"
include "include/PLAYERINFO.ts"
include "include/_PAD_LIB.ts"
include "include/_VID_STARTUP.ts"
include "include/TERRAKIT.ts"
include "include/PLAYERINFO.ts"
include "include/MAKEGLOBLES.ts"
