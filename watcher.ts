systemscript




loadvar $map~stardock
loadvar $bot~subspace
loadvar $bot~bot_password
loadvar $bot~bot_name
loadvar $bot~last_fighter_attack
loadvar $bot~mombot_directory
if ($bot~last_fighter_attack = 0)
  setvar $bot~last_fighter_attack 1
end

setsectorparameter 1 "MSLSEC" TRUE
setsectorparameter 2 "MSLSEC" TRUE
setsectorparameter 3 "MSLSEC" TRUE
setsectorparameter 4 "MSLSEC" TRUE
setsectorparameter 5 "MSLSEC" TRUE
setsectorparameter 6 "MSLSEC" TRUE
setsectorparameter 7 "MSLSEC" TRUE
setsectorparameter 8 "MSLSEC" TRUE
setsectorparameter 9 "MSLSEC" TRUE
setsectorparameter 10 "MSLSEC" TRUE
if ($map~stardock > 0)
  setsectorparameter $map~stardock "MSLSEC" TRUE
end


settextlinetrigger FEDERASE :FEDERASEFIG "The Federation We destroyed your Corp's "
settextlinetrigger FIGHTERSERASE :ERASEFIG " of your fighters in sector "
settextlinetrigger FIGHTERSAVE :FIGHTERSAVE "Deployed Fighters "
settextlinetrigger LIMPSAVE :LIMPSAVE "Limpet mine in "
settextlinetrigger ARMIDSAVE :ARMIDSAVE "Your mines in "
settextlinetrigger WARPFIGERASE :ERASEWARPFIG "You do not have any fighters in Sector "
settextlinetrigger PGRIDADD :PGRIDADD "Successfully P-gridded into sector "
settextlinetrigger PGRIDXPORTADD :PGRIDXPORTADD "Successfully P-gridded w/xport into sector "
settextlinetrigger PGRIDREMOVE :PGRIDREMOVE "Unsuccessful P-grid into sector "
settextlinetrigger CLEARBUSTS :ERASEBUSTS ">[Busted:"
settextlinetrigger ADDFIGS :ADDFIGS ">[Figged:"
settextlinetrigger PLANETMOVED :UPDATEPLANETMOVEMENT " moved to sector "
settextlinetrigger FIGHTERSADD :ADDFIG "Should they be (D)efensive, (O)ffensive or Charge a (T)oll ?"
settextlinetrigger GETPLANETNUMBER :SETPLANETNUMBER "Planet #"
settexttrigger SECTORDATA :CHECKSECTORDATA "(?=Help)? :"
settextlinetrigger GETSHIPSTATS :SETSHIPOFFENSIVEODDS "Offensive Odds: "
settextlinetrigger GETSHIPMAXFIGHTERS :SETSHIPMAXFIGATTACK " TransWarp Drive:   "
settextlinetrigger CAPTURELEVELPLANET :CAPTURELEVELPLANET " Level "
settextlinetrigger CAPTURENOLEVELPLANET :CAPTURENOLEVELPLANET " No Citadel"
settextlinetrigger EMERGENCY_REBOOT :EMERGENCY_REBOOT "<EMERGENCY REBOOT>"&$bot~bot_password
settextlinetrigger SHIPDESTROYED :SHIPDESTROYED "You will have to start over from scratch!"
settextlinetrigger GETPLANETNUMBERRAW :SETPLANETNUMBERRAW "Land on which planet <Q to abort> ? "
settextlinetrigger GETSHIPNUMBERRAW :SETSHIPNUMBERRAW "Choose which ship to beam to (Q=Quit) "
killtrigger CHECKIFBOTALIVE
setdelaytrigger CHECKIFBOTALIVE :CHECKIFBOTALIVE 60000
settextlinetrigger LRACHECK :LRACHECK "For stealing from this port, your alignment"
settextlinetrigger LRACHECK2 :LRACHECK "For robbing this port, your alignment"
settextlinetrigger BUSTED :BUSTED "For getting caught your alignment went down by"
settextlinetrigger FAKEBUSTED :FAKEBUSTED "(You realize the guards saw you last time!)"
settextlinetrigger MANUALSUBSPACE :MANUALSUBSPACE "Ok, you will send and receive sub-space messages on channel "
settextlinetrigger FOUNDBIGBUBBLE :FOUNDBIGBUBBLE "[Found Big Bubble]"
settextlinetrigger FOUNDBIGTUNNEL :FOUNDBIGTUNNEL "[Found Big Tunnel]"
settextlinetrigger FERRENGIHITCORP :FERRENGIHITCORP "Your Corp's fighters in sector "
settextlinetrigger FERRENGIHITPERS :FERRENGIHITPERS "Your fighters in sector "
pause
:FOUNDBIGBUBBLE


gettext CURRENTLINE $BSEC " Door: " " Internal Sec:"
isnumber $TEST $BSEC
if ($TEST = TRUE)
  getsectorparameter $BSEC "BUBBLEDOOR" $PARAM_TUNNEL
  if ($PARAM_TUNNEL = "")
    setvar $PARAM_TUNNEL FALSE
  end
  if ($PARAM_TUNNEL = FALSE)
    setsectorparameter $BSEC "BUBBLEDOOR" 1
    gettext CURRENTLINE $INT "Internal Sec:" ""
    setsectorparameter $BSEC "BUBBLEINT" $INT
  end
end
settextlinetrigger FOUNDBIGBUBBLE :FOUNDBIGBUBBLE "[Found Big Bubble]"
pause
:FOUNDBIGTUNNEL
gettext CURRENTLINE $DSEC1 "Door 1: " " Door 2:"
gettext CURRENTLINE $DSEC2 "Door 2: " " Internal"
isnumber $TEST $DSEC1
if ($TEST = TRUE)
  getsectorparameter $DSEC1 "TUNNELDOOR" $PARAM_TUNNEL

  if ($PARAM_TUNNEL = "")
    setvar $PARAM_TUNNEL FALSE
  end
  if ($PARAM_TUNNEL = FALSE)
    setsectorparameter $DSEC1 "TUNNELDOOR" 1
    setsectorparameter $DSEC2 "TUNNELDOOR" 1
    gettext CURRENTLINE $INT "Internal Sec:" ""
    setsectorparameter $DSEC1 "TUNNELINT" $INT
    setsectorparameter $DSEC2 "TUNNELINT" $INT
  end
end
settextlinetrigger FOUNDBIGTUNNEL :FOUNDBIGTUNNEL "[Found Big Tunnel]"
pause
:MANUALSUBSPACE
gettext CURRENTLINE&"  [XX][XX][XX]" $bot~subspace "Ok, you will send and receive sub-space messages on channel " " now.  [XX][XX][XX]"
savevar $bot~subspace
settextlinetrigger MANUALSUBSPACE :MANUALSUBSPACE "Ok, you will send and receive sub-space messages on channel "
pause
:BUSTED

loadvar $player~current_sector
setsectorparameter $player~current_sector "BUSTED" TRUE
setsectorparameter 1 "LRA" $player~current_sector
settextlinetrigger BUSTED :BUSTED "For getting caught your alignment went down by"
pause
:FAKEBUSTED

loadvar $player~current_sector
setsectorparameter $player~current_sector "FAKEBUST" TRUE
settextlinetrigger FAKEBUSTED :FAKEBUSTED "(You realize the guards saw you last time!)"
pause
:LRACHECK

killtrigger LRACHECK
killtrigger LRACHECK2
loadvar $player~current_sector
setsectorparameter 1 "LRA" $player~current_sector
settextlinetrigger LRACHECK :LRACHECK "For stealing from this port, your alignment"
settextlinetrigger LRACHECK2 :LRACHECK "For robbing this port, your alignment"
pause
:SETSHIPNUMBERRAW

getword CURRENTLINE $SPOOF 1
if ($SPOOF = "Choose")
  getword CURRENTLINE $player~ship_number 8
  isnumber $TEST $player~ship_number
  if ($TEST = TRUE)
    savevar $player~ship_number
  end
end
settextlinetrigger GETSHIPNUMBERRAW :SETSHIPNUMBERRAW "Choose which ship to beam to (Q=Quit) "
pause

pause
:SETPLANETNUMBERRAW


getword CURRENTLINE $SPOOF 1
if ($SPOOF = "Land")
  getword CURRENTLINE $planet~planet 9
  isnumber $TEST $planet~planet
  if ($TEST = TRUE)
    savevar $planet~planet
  end
end
settextlinetrigger GETPLANETNUMBERRAW :SETPLANETNUMBERRAW "Land on which planet <Q to abort> ? "
pause

pause
:FEDERASEFIG

getword CURRENTLINE $SPOOF 1
if ($SPOOF <> "The")
  goto :ENDFEDERASEFIG
end
gettext CURRENTLINE&"  [XX][XX][XX]" $TEMP " fighters in sector " ".  [XX][XX][XX]"
if ($TEMP <> "")
  isnumber $TEST $TEMP
  if ($TEST = TRUE)
    if (($TEMP <= SECTORS) and ($TEMP > 0))
      setvar $TARGET $TEMP
      setsectorparameter $TARGET "MSLSEC" TRUE
      gosub :REMOVEFIGFROMDATA
    end
  end
end
:ENDFEDERASEFIG
settextlinetrigger FEDERASE :FEDERASEFIG "The Federation We destroyed "
pause
:ERASEFIG
setvar $LINE CURRENTLINE
setvar $ANSI_LINE CURRENTANSILINE
cuttext $LINE&"     " $SPOOF 1 2
cuttext $LINE&"     " $SPOOF2 1 1
if (($SPOOF = "R ") or ($SPOOF = "F ") or ($SPOOF = "P ") or ($SPOOF2 = "'") or ($SPOOF2 = "`"))
  goto :ENDERASEFIG
end
gettext $LINE&" [XX][XX][XX]" $TEMP " destroyed " " [XX][XX][XX]"
if ($TEMP <> "")
  getword $TEMP $FIG_HIT 7
  getword $TEMP $FIG_NUMBER 1
  isnumber $TEST $FIG_HIT
  if (($TEST = TRUE) and ($FIG_NUMBER <> 0))
    if (($FIG_HIT <= SECTORS) and ($FIG_HIT > 0))
      setvar $TARGET $FIG_HIT
      setvar $bot~last_fighter_hit $FIG_HIT
      setvar $bot~last_hit $FIG_HIT
      savevar $bot~last_fighter_hit
      savevar $bot~last_hit
      gosub :REMOVEFIGFROMDATA
    end
  end
end
:ENDERASEFIG
settextlinetrigger FIGHTERSERASE :ERASEFIG " of your fighters in sector "
pause
:ERASEWARPFIG
getword CURRENTLINE $SPOOF 1
if ($SPOOF <> "You")
  settextlinetrigger WARPFIGERASE :ERASEWARPFIG "You do not have any fighters in Sector "
  pause
end
gettext CURRENTLINE&" [XX][XX][XX]" $TEMP "You do not have any fighters in Sector " ". [XX][XX][XX]"
if ($TEMP <> "")
  isnumber $TEST $TEMP
  if ($TEST)
    if (($TEMP <= SECTORS) and ($TEMP > 0))
      setvar $TARGET $TEMP
      gosub :REMOVEFIGFROMDATA
    end
  end
end
settextlinetrigger WARPFIGERASE :ERASEWARPFIG "You do not have any fighters in Sector "
pause
:LIMPSAVE
setvar $LINE CURRENTLINE
cuttext $LINE&"     " $SPOOF 1 2
cuttext $LINE&"     " $SPOOF2 1 1
if (($SPOOF = "R ") or ($SPOOF = "F ") or ($SPOOF = "P ") or ($SPOOF2 = "'") or ($SPOOF2 = "`"))
  goto :ENDSAVELIMP
end
gettext $LINE&" [XX][XX][XX]" $TEMP "Limpet mine in " " activated"
if ($TEMP <> "")
  setvar $LIMP_HIT $TEMP
  isnumber $TEST $LIMP_HIT
  if ($TEST = TRUE)
    if (($LIMP_HIT <= SECTORS) and ($LIMP_HIT > 0))
      setvar $bot~last_limpet_attack $LINE
      savevar $bot~last_limpet_attack
      setvar $bot~last_hit_type "limpet"
      savevar $bot~last_hit_type
      setvar $bot~last_limpet_hit $LIMP_HIT
      setvar $bot~last_hit $LIMP_HIT
      savevar $bot~last_hit
      savevar $bot~last_limpet_hit
    end
  end
end
:ENDSAVELIMP
settextlinetrigger LIMPSAVE :LIMPSAVE "Limpet mine in "
pause
:ARMIDSAVE

setvar $LINE CURRENTLINE
setvar $ANSI_LINE CURRENTANSILINE
cuttext $LINE&"     " $SPOOF 1 2
cuttext $LINE&"     " $SPOOF2 1 1
if (($SPOOF = "R ") or ($SPOOF = "F ") or ($SPOOF = "P ") or ($SPOOF2 = "'") or ($SPOOF2 = "`"))
  goto :ENDSAVEARMID
end

gettext $LINE&" [XX][XX][XX]" $TEMP "Your mines in " " did "
if ($TEMP <> "")
  setvar $MINE_HIT $TEMP
  isnumber $TEST $MINE_HIT
  if ($TEST = TRUE)
    if (($MINE_HIT <= SECTORS) and ($MINE_HIT > 0))
      setvar $bot~last_armid_attack $LINE
      setvar $bot~ansi_last_armid_attack $ANSI_LINE
      savevar $bot~last_armid_attack
      savevar $bot~ansi_last_armid_attack
      setvar $bot~last_hit_type "armid"
      savevar $bot~last_hit_type
      setvar $bot~last_armid_hit $MINE_HIT
      setvar $bot~last_hit $MINE_HIT
      savevar $bot~last_hit
      savevar $bot~last_armid_hit
    end
  end
end
:ENDSAVEARMID
settextlinetrigger ARMIDSAVE :ARMIDSAVE "Your mines in "
pause
:FIGHTERSAVE



setvar $LINE CURRENTLINE
setvar $ANSI_LINE CURRENTANSILINE
cuttext $LINE&"     " $SPOOF 1 2
cuttext $LINE&"     " $SPOOF2 1 1
if (($SPOOF = "R ") or ($SPOOF = "F ") or ($SPOOF = "P ") or ($SPOOF2 = "'") or ($SPOOF2 = "`"))
  goto :ENDFIGHTERSAVE
end

gettext $LINE&" [XX][XX][XX]" $TEMP "Deployed Fighters Report Sector " ": "
if ($TEMP <> "")
  setvar $FIGHIT $TEMP
  isnumber $TEST $FIGHIT
  if ($TEST = TRUE)
    if (($FIGHIT <= SECTORS) and ($FIGHIT > 0))
      setvar $bot~last_hit_type "fighter"
      savevar $bot~last_hit_type
      setvar $bot~last_fighter_attack $LINE
      savevar $bot~last_fighter_attack
      setvar $bot~ansi_last_fighter_attack $ANSI_LINE
      savevar $bot~ansi_last_fighter_attack
      setvar $bot~last_fighter_hit $FIGHIT
      setvar $bot~last_hit $FIGHIT
      savevar $bot~last_hit
      savevar $bot~last_fighter_hit
    end
  end
end
:ENDFIGHTERSAVE
settextlinetrigger FIGHTERSAVE :FIGHTERSAVE "Deployed Fighters "
pause
:ERASEBUSTS

loadvar $bot~subspace
cuttext CURRENTLINE&"   " $SPOOF 1 1
getwordpos CURRENTLINE $POS "<"&$bot~subspace&">["
getwordpos CURRENTLINE $POS2 "]<"&$bot~subspace&">"
if (($POS <= 0) or ($POS2 <= 0))
  setvar $SPOOF TRUE
end
if ($SPOOF <> "R")
  settextlinetrigger CLEARBUSTS :ERASEBUSTS ">[Busted:"
  pause
end
gettext CURRENTLINE&" [XX][XX][XX]" $TEMP ">[Busted:" "]<"

if ($TEMP <> "")
  isnumber $TEST $TEMP
  if ($TEST)
    if (($TEMP <= SECTORS) and ($TEMP > 0))
      setsectorparameter $TEMP "BUSTED" FALSE
      setsectorparameter $TEMP "FAKEBUST" FALSE
    end
  end
end
settextlinetrigger CLEARBUSTS :ERASEBUSTS ">[Busted:"
pause
:ADDFIGS

loadvar $bot~subspace
cuttext CURRENTLINE&"   " $SPOOF 1 1
getwordpos CURRENTLINE $POS "<"&$bot~subspace&">["
getwordpos CURRENTLINE $POS2 "]<"&$bot~subspace&">"
if (($POS <= 0) or ($POS2 <= 0))
  setvar $SPOOF TRUE
end
if ($SPOOF <> "R")
  settextlinetrigger ADDFIGS :ADDFIGS ">[Figged:"

  pause
end
gettext CURRENTLINE&" [XX][XX][XX]" $TEMP ">[Figged:" "]<"
if ($TEMP <> "")
  setvar $JUNK "JUNKJUNK"
  setvar $I 1
  :CHECK_FIGS_AGAIN
  getword $TEMP $TEMP_SECTOR $I $JUNK
  if ($TEMP_SECTOR <> $JUNK)
    isnumber $TEST $TEMP_SECTOR
    if ($TEST)
      setsectorparameter $TEMP_SECTOR "FIGSEC" TRUE
      getsectorparameter 2 "FIG_COUNT" $FIGCOUNT
      setsectorparameter 2 "FIG_COUNT" ($FIGCOUNT + 1)
    end
    add $I 1
    goto :CHECK_FIGS_AGAIN
  end
end
settextlinetrigger ADDFIGS :ADDFIGS ">[Figged:"
pause
:UPDATEPLANETMOVEMENT

cuttext CURRENTLINE&"   " $SPOOF 1 1
if ($SPOOF <> "R")
  settextlinetrigger PLANETMOVED :UPDATEPLANETMOVEMENT " moved to sector "
  pause
end
getwordpos CURRENTLINE $POS "} - Planet #"
getwordpos CURRENTLINE $POS2 " moved to sector "
if (($POS > 0) and ($POS2 > 0))
  getword CURRENTLINE $planet~planet_id 6
  getword CURRENTLINE $planet~planet_sector 10
  replacetext $planet~planet_id "#" ""
  replacetext $planet~planet_sector "." ""
  isnumber $TEST $planet~planet_sector
  if ($TEST)
    setsectorparameter $planet~planet_id "PSECTOR" $planet~planet_sector
  end
end
settextlinetrigger PLANETMOVED :UPDATEPLANETMOVEMENT " moved to sector "
pause
:PGRIDADD

cuttext CURRENTLINE&"   " $SPOOF 1 1
if ($SPOOF <> "R")
  settextlinetrigger PGRIDADD :PGRIDADD "Successfully P-gridded into sector "
  pause
end

gettext CURRENTLINE&" [XX][XX][XX]" $TEMP "Successfully P-gridded into sector " " [XX][XX][XX]"
if ($TEMP <> "")
  isnumber $TEST $TEMP
  if ($TEST)
    if (($TEMP <= SECTORS) and ($TEMP > 0))
      setvar $TARGET $TEMP
      gosub :ADDFIGTODATA
    end
  end
end
settextlinetrigger PGRIDADD :PGRIDADD "Successfully P-gridded into sector "
pause
:PGRIDXPORTADD

cuttext CURRENTLINE&"   " $SPOOF 1 1
if ($SPOOF <> "R")
  settextlinetrigger PGRIDXPORTADD :PGRIDXPORTADD "Successfully P-gridded w/xport into sector "
  pause
end

gettext CURRENTLINE&" [XX][XX][XX]" $TEMP "Successfully P-gridded w/xport into sector " " [XX][XX][XX]"
if ($TEMP <> "")
  isnumber $TEST $TEMP
  if ($TEST)
    if (($TEMP <= SECTORS) and ($TEMP > 0))
      setvar $TARGET $TEMP
      gosub :ADDFIGTODATA
    end
  end
end
settextlinetrigger PGRIDXPORTADD :PGRIDXPORTADD "Successfully P-gridded w/xport into sector "
pause
:PGRIDREMOVE

cuttext CURRENTLINE&"   " $SPOOF 1 1
if ($SPOOF <> "R")
  settextlinetrigger PGRIDREMOVE :PGRIDREMOVE "Unsuccessful P-grid into sector "
  pause
end

gettext CURRENTLINE&" [XX][XX][XX]" $TEMP "Unsuccessful P-grid into sector " ". Someone make sure bot is picked up."
if ($TEMP <> "")
  isnumber $TEST $TEMP
  if ($TEST)
    if (($TEMP <= SECTORS) and ($TEMP > 0))
      setvar $TARGET $TEMP
      gosub :REMOVEFIGFROMDATA
    end
  end
end
settextlinetrigger PGRIDREMOVE :PGRIDREMOVE "Unsuccessful P-grid into sector "
pause
:FERRENGIHITCORP

setvar $LINE CURRENTLINE
cuttext $LINE&"     " $SPOOF 1 2
cuttext $LINE&"     " $SPOOF2 1 1
if (($SPOOF = "R ") or ($SPOOF = "F ") or ($SPOOF = "P ") or ($SPOOF2 = "'") or ($SPOOF2 = "`"))
  goto :ENDFERRENGIHITCORP
end

gettext $LINE&" [XX][XX][XX]" $TEMP "Your Corp's fighters in sector " " lost "
if ($TEMP <> "")
  setvar $TARGET $TEMP
  isnumber $TEST $TARGET
  if ($TEST = TRUE)
    if (($TARGET <= SECTORS) and ($TARGET > 0))
      gosub :REMOVEFIGFROMDATA
    end
  end
end
:ENDFERRENGIHITCORP
settextlinetrigger FERRENGIHITCORP :FERRENGIHITCORP "Your Corp's fighters in sector "
pause
:FERRENGIHITPERS

setvar $LINE CURRENTLINE
cuttext $LINE&"     " $SPOOF 1 2
cuttext $LINE&"     " $SPOOF2 1 1
if (($SPOOF = "R ") or ($SPOOF = "F ") or ($SPOOF = "P ") or ($SPOOF2 = "'") or ($SPOOF2 = "`"))
  goto :ENDFERRENGIHITPERS
end

gettext $LINE&" [XX][XX][XX]" $TEMP "Your fighters in sector " " lost "
if ($TEMP <> "")
  setvar $TARGET $TEMP
  isnumber $TEST $TARGET
  if ($TEST = TRUE)
    if (($TARGET <= SECTORS) and ($TARGET > 0))
      gosub :REMOVEFIGFROMDATA
    end
  end
end
:ENDFERRENGIHITPERS
settextlinetrigger FERRENGIHITPERS :FERRENGIHITPERS "Your fighters in sector "
pause
:ADDFIG

isnumber $TEST CURRENTSECTOR
if ($TEST)
  if ((CURRENTSECTOR > 10) and (CURRENTSECTOR < SECTORS))
    setvar $TARGET CURRENTSECTOR
    gosub :ADDFIGTODATA
  end
end
settextlinetrigger FIGHTERSADD :ADDFIG "Should they be (D)efensive, (O)ffensive or Charge a (T)oll ?"
pause
:REMOVEFIGFROMDATA



getsectorparameter $TARGET "FIGSEC" $CHECK
if ($CHECK = TRUE)
  getsectorparameter 2 "FIG_COUNT" $FIGCOUNT
  setsectorparameter 2 "FIG_COUNT" ($FIGCOUNT - 1)
end
setsectorparameter $TARGET "FIGSEC" FALSE
return
:ADDFIGTODATA
getsectorparameter $TARGET "FIGSEC" $CHECK
if ($CHECK <> TRUE)
  getsectorparameter 2 "FIG_COUNT" $FIGCOUNT
  setsectorparameter 2 "FIG_COUNT" ($FIGCOUNT + 1)
end
setsectorparameter $TARGET "FIGSEC" TRUE
return
:SETPLANETNUMBER



getwordpos RAWPACKET $POS "Planet "&#27&"[1;33m#"&#27&"[36m"
if ($POS > 0)
  gettext RAWPACKET $planet~planet "Planet "&#27&"[1;33m#"&#27&"[36m" #27&"[0;32m in sector "
  isnumber $TEST $planet~planet
  if ($TEST = TRUE)
    savevar $planet~planet
    setsectorparameter $planet~planet "PSECTOR" CURRENTSECTOR
  end
end
settextlinetrigger GETPLANETNUMBER :SETPLANETNUMBER "Planet #"
pause
:CHECKSECTORDATA


gettext CURRENTLINE $CURSEC "]:[" "] ("
if ($CURSEC = CURRENTSECTOR)
  setvar $player~current_sector $CURSEC
  savevar $player~current_sector
  getsectorparameter $player~current_sector "BUSTED" $ISBUSTED
  loadvar $bot~command_prompt_extras
  if (($bot~command_prompt_extras = TRUE) and ($ISBUSTED = TRUE))
    echo ANSI_5 "[" ANSI_12 "BUSTED" ANSI_5 "] : "
  end
  getsectorparameter $player~current_sector "MSLSEC" $ISMSL
  if (($bot~command_prompt_extras = TRUE) and ($ISMSL = TRUE))
    echo ANSI_5 "[" ANSI_9 "MSL" ANSI_5 "] : "
  end
end
settexttrigger SECTORDATA :CHECKSECTORDATA "(?=Help)? :"
pause
:SETSHIPOFFENSIVEODDS


getwordpos CURRENTANSILINE $POS "[0;31m:[1;36m1"
if ($POS > 0)
  gettext CURRENTANSILINE $ship~ship_offensive_odds "Offensive Odds[1;33m:[36m " "[0;31m:[1;36m1"
  striptext $ship~ship_offensive_odds "."
  striptext $ship~ship_offensive_odds " "
  savevar $ship~ship_offensive_odds
  gettext CURRENTANSILINE $ship~ship_fighters_max "Max Fighters[1;33m:[36m" "[0;32m Offensive Odds"
  striptext $ship~ship_fighters_max ","
  striptext $ship~ship_fighters_max " "
  savevar $ship~ship_fighters_max
end
settextlinetrigger GETSHIPSTATS :SETSHIPOFFENSIVEODDS "Offensive Odds: "
pause
:SETSHIPMAXFIGATTACK
getwordpos CURRENTANSILINE $POS "[0m[32m Max Figs Per Attack[1;33m:[36m"
if ($POS > 0)
  gettext CURRENTANSILINE $ship~ship_max_attack "[0m[32m Max Figs Per Attack[1;33m:[36m" "[0;32mTransWarp"
  striptext $ship~ship_max_attack " "
  savevar $ship~ship_max_attack
end
settextlinetrigger GETSHIPMAXFIGHTERS :SETSHIPMAXFIGATTACK " TransWarp Drive:   "
pause

return
:CAPTURELEVELPLANET


getwordpos CURRENTANSILINE $POS "[32mLevel [1;33m"
if ($POS > 0)
  getword CURRENTLINE $planet~planet_sector 1
  getword CURRENTLINE $planet~planet_id 2
  if ($planet~planet_id = "T")
    getword CURRENTLINE $planet~planet_id 3
  end
  replacetext $planet~planet_id "#" ""
  isnumber $TEST $planet~planet_id
  getwordpos $planet~planet_id $POS "."
  if (($TEST = TRUE) and ($POS <= 0))
    if ($planet~planet_id > 0)
      setsectorparameter $planet~planet_id "PSECTOR" $planet~planet_sector
    end
  end
end
settextlinetrigger CAPTURELEVELPLANET :CAPTURELEVELPLANET " Level "
pause
:CAPTURENOLEVELPLANET

getwordpos CURRENTANSILINE $POS "[32m No Citadel"
if ($POS > 0)
  getword CURRENTLINE $planet~planet_sector 1
  getword CURRENTLINE $planet~planet_id 2
  if ($planet~planet_id = "T")
    getword CURRENTLINE $planet~planet_id 3
  end
  replacetext $planet~planet_id "#" ""
  isnumber $TEST $planet~planet_id
  getwordpos $planet~planet_id $POS "."
  if (($TEST = TRUE) and ($POS <= 0))
    if ($planet~planet_id > 0)
      setsectorparameter $planet~planet_id "PSECTOR" $planet~planet_sector
    end
  end
end
settextlinetrigger CAPTURENOLEVELPLANET :CAPTURENOLEVELPLANET " No Citadel"
pause
:SHIPDESTROYED


getwordpos CURRENTANSILINE $POS "[32mYou will have to start over"
if ($POS > 0)
  setvar $bot~isshipdestroyed TRUE
  savevar $bot~isshipdestroyed
  if (ISNATIVEBOT = TRUE)
    setvar $bot~do_not_resuscitate TRUE
    savevar $bot~do_not_resuscitate
    echo "Mombot stopped: ship destroyed.**"
    nativebot "STOP"
    halt
  end
  disconnect
  setvar $I 1
  setvar $FOUND FALSE
  setvar $REBOOTED FALSE
  echo "Mombot rebooting..**"
  setdelaytrigger WAITFORREBOOTLIST :LISTOKAYNOW 1500
  pause
  :LISTOKAYNOW
  listactivescripts $SCRIPTS
  while ($I <= $SCRIPTS)
    getwordpos "<><><>"&$SCRIPTS[$I] $POS "<><><>mombot"
    if ($POS > 0)
      if ($REBOOTED = FALSE)
        setdelaytrigger WAITFORREBOOT :OKAYNOW 3000
        pause
        :OKAYNOW
        load "scripts\"&$bot~mombot_directory&"\"&$SCRIPTS[$I]
        setvar $REBOOTED TRUE
      end
      stop $SCRIPTS[$I]
    end

    add $I 1
  end
  if ($FOUND = FALSE)
    echo "No mombot script found to reboot.**"
  end
end

settextlinetrigger SHIPDESTROYED :SHIPDESTROYED "You will have to start over from scratch!"
pause
:EMERGENCY_REBOOT

loadvar $bot~subspace
loadvar $bot~bot_name
loadvar $bot~bot_password
getwordpos CURRENTLINE $POS $bot~bot_name&" "&$bot~subspace&"<EMERGENCY REBOOT>"&$bot~bot_password
if ($POS <= 0)
  settextlinetrigger EMERGENCY_REBOOT :EMERGENCY_REBOOT "<EMERGENCY REBOOT>"&$bot~bot_password
  pause
end
setvar $I 1
setvar $FOUND FALSE
setvar $REBOOTED FALSE
setdelaytrigger LISTOKAYNOWEMERGENCY :LISTOKAYNOWEMERGENCY 1500
pause
:LISTOKAYNOWEMERGENCY
listactivescripts $SCRIPTS
while ($I <= $SCRIPTS)
  getwordpos "<><><>"&$SCRIPTS[$I] $POS "mombot"
  if ($POS > 0)
    stop $SCRIPTS[$I]
  end




  add $I 1
end
while ($FOUND = FALSE)
  echo "No mombot script found to kill, so assuming default of mombot.cts*"
  setvar $BOOT_THIS "mombot.cts"
end
setdelaytrigger OKAYNOWEMERGENCY :OKAYNOWEMERGENCY 3000
pause
:OKAYNOWEMERGENCY
load "scripts\"&$bot~mombot_directory&"\"&$BOOT_THIS
settextlinetrigger EMERGENCY_REBOOT :EMERGENCY_REBOOT "<EMERGENCY REBOOT>"&$bot~bot_password
pause
:CHECKIFBOTALIVE

loadvar $bot~do_not_resuscitate
loadvar $map~stardock
loadvar $bot~subspace
loadvar $bot~bot_password
loadvar $bot~bot_name

if (ISNATIVEBOT = TRUE)
  killtrigger CHECKIFBOTALIVE
  setdelaytrigger CHECKIFBOTALIVE :CHECKIFBOTALIVE 60000
  pause
end

if ($bot~do_not_resuscitate <> TRUE)
  setvar $FOUND FALSE
  listactivescripts $SCRIPTS
  setvar $I 1
  while (($I <= $SCRIPTS) and ($FOUND = FALSE))
    getwordpos "<><><>"&$SCRIPTS[$I] $POS "mombot"
    if ($POS > 0)
      if ($FOUND = FALSE)
        setvar $FOUND TRUE
      end
    end
    add $I 1
  end
  if ($FOUND = FALSE)
    echo "**"&ANSI_2&"["&ANSI_4&"No mombot is running, automatically booting up mombot."&ANSI_2&"]**"
    load "scripts\"&$bot~mombot_directory&"\mombot.cts"
  end
  killtrigger CHECKIFBOTALIVE
  setdelaytrigger CHECKIFBOTALIVE :CHECKIFBOTALIVE 60000
  pause
end
