










































reqrecording
clearallavoids
gosub :bot~loadvars

loadvar $map~rylos
loadvar $map~alpha_centauri
loadvar $bot~limp_file
loadvar $bot~armid_file

setvar $bot~help[1] $bot~tab&"       LS Passive Gridder - Still the best "
setvar $bot~help[2] $bot~tab&"       "
setvar $bot~help[3] $bot~tab&" lspassgrid [stopturns] {a1/a2/a3} {l1/l2/l3} {ports}"
setvar $bot~help[4] $bot~tab&"            {holo} {trade} {restock} {filter} {ignore:}"
setvar $bot~help[5] $bot~tab&" Options:"
setvar $bot~help[6] $bot~tab&"    [stopturns]     Passive Grid Stops at here"
setvar $bot~help[7] $bot~tab&"\t   {a1/a2/a3}      Drop 1/2/3 Armid Mines"
setvar $bot~help[8] $bot~tab&"\t   {l1/l2/l3}      Drop 1/2/3 Limpet Mines"
setvar $bot~help[9] $bot~tab&"    {ports}         Grabs port reports"
setvar $bot~help[10] $bot~tab&"    {holo}         Holo Scans to ensure sectors safe"
setvar $bot~help[11] $bot~tab&"    {trade}        Will trade ports looking for Equ MCIC"
setvar $bot~help[12] $bot~tab&"                   Requires EP Haggle or equiv"
setvar $bot~help[13] $bot~tab&"    {safe}         Twarps to Limpet sectors only"
setvar $bot~help[14] $bot~tab&"    {paranoid}     Twarp to Limpet and Mines only"
setvar $bot~help[15] $bot~tab&"    {nextreport}   Next sector requires an adj port report."
setvar $bot~help[16] $bot~tab&"    {restock}      Buys more Limpets and Mines."
setvar $bot~help[17] $bot~tab&"    {filter}       Filters mines/armids/planets to detect"
setvar $bot~help[18] $bot~tab&"                   safe sectors. run >limps >armids 1st"
setvar $bot~help[19] $bot~tab&"    {ignorea}      Uses holo scan to passive grid alien figs"
setvar $bot~help[20] $bot~tab&"    {resume}       Roughly resumes last run"
setvar $bot~help[21] $bot~tab&"    {ignore:}      Ignore corp or trader fighters"
setvar $bot~help[22] $bot~tab&"    {skip:}        Skips sectors with this param !=0 !=''"
setvar $bot~help[23] $bot~tab&"    {lock:PARAM=n} Lock grid to this param - WHICHBUB=2"
setvar $bot~help[24] $bot~tab&"    {twenty}       Drop 20 fighters in density 0 sectors"
setvar $bot~help[25] $bot~tab&"    Doesn't require ZTM but works better"
setvar $bot~help[26] $bot~tab&"    Works best with T-Warp to reroute"

gosub :bot~helpfile

setvar $bot~script_title "LoneStar's Passive Gridder"
gosub :bot~banner

setvar $TAGLINE "LoneStar's Passive Gridder"
setvar $TAGLINEB $bot~bot_name
setvar $TAGLINEC $bot~bot_name

setvar $TURN_LIMIT 20
setarray $CHKD SECTORS
setarray $ANOM 10
setarray $DENS 10
setarray $LIMPS SECTORS
setvar $UPDATE_LIMPS FALSE
setvar $UPDATE_FIGS FALSE
setvar $UPDATE_PORT FALSE

setvar $DROPING_MINES 0
setvar $DROP_LIMP 0
setvar $DROP_ARMID 0


setarray $LOG_ENTRIES 5
setvar $LOG_ENTRIES[1] ""
setvar $LOG_ENTRIES[2] ""
setvar $LOG_ENTRIES[3] ""
setvar $LOG_ENTRIES[4] ""
setvar $LOG_ENTRIES[5] ""

setvar $DEP_FIGS 0
setvar $DEP_LIMP 0
setvar $DEP_NEW 0
setvar $LOG_EVENT 0
setvar $HOLO FALSE
setvar $TRACKER FALSE
setvar $EQU_MIN 50
setvar $EQU_MIN_BUY 25
setvar $DROP_TWENTY 0
setvar $FILTER_DENSITY 0

setvar $planet~planetsinsectors SECTORS



if ($map~rylos < 1)
  setvar $REPORT_RYLOS TRUE
end
if ($map~alpha_centauri < 1)
  setvar $REPORT_ALPHA TRUE
end
setvar $player~save TRUE

gosub :player~quikstats
setvar $UNLIM $player~unlimitedgame
if ($player~total_holds <= $EQU_MIN)
end


setvar $STARTINGLOCATION $player~current_prompt
if ($STARTINGLOCATION <> "Command")
  setvar $switchboard~message "Must be started from Command prompt.*"
  gosub :switchboard~switchboard
  halt
end

if ($player~scan_type = "None")
  setvar $switchboard~message "Must At Least Have a Density Scanner.*"
  gosub :switchboard~switchboard
  halt
end
if ($player~fighters < 10)

  setvar $switchboard~message "Must At More than 10 Fighters.*"
  gosub :switchboard~switchboard
  halt
end
if ($player~credits < 10000)

  setvar $switchboard~message "Must At Least Have 10,000 creds.*"
  gosub :switchboard~switchboard
  halt
end


setvar $UPDATE_FIGS FALSE
setvar $UPDATE_LIMPS FALSE



setvar $TURN_LIMIT $bot~parm1
isnumber $NUMBER $TURN_LIMIT

if (($UNLIM = FALSE) and (($NUMBER <> 1) or ($TURN_LIMIT = 0)))
  setvar $switchboard~message "Please select what turns to halt at.*"
  gosub :switchboard~switchboard
  halt
end


getwordpos $bot~user_command_line $POS "ignore:"
if ($POS > 0)
  gettext $bot~user_command_line $IGNORE "ignore:" " "

  if ($IGNORE = "")
    setvar $bot~user_command_line $bot~user_command_line&" "
    gettext $bot~user_command_line $IGNORE "ignore:" " "
  end
  replacetext $bot~user_command_line " ignore:"&$IGNORE&" " " "
  replacetext $bot~user_command_line " ignore:"&$IGNORE " "
end

getwordpos $bot~user_command_line $POS "skip:"
if ($POS > 0)
  gettext $bot~user_command_line $SKIPPARAM "skip:" " "

  if ($SKIPPARAM = "")
    setvar $bot~user_command_line $bot~user_command_line&" "
    gettext $bot~user_command_line $SKIPPARAM "skip:" " "
  end
  replacetext $bot~user_command_line " skip:"&$SKIPPARAM&" " " "
  replacetext $bot~user_command_line " skip:"&$SKIPPARAM " "
  uppercase $SKIPPARAM
end

getwordpos $bot~user_command_line $POS "lock:"
if ($POS > 0)
  gettext $bot~user_command_line $LOCKPARAMTEMP "lock:" " "

  if ($LOCKPARAMTEMP = "")
    setvar $bot~user_command_line $bot~user_command_line&" "
    gettext $bot~user_command_line $LOCKPARAMTEMP "lock:" " "
  end

  replacetext $bot~user_command_line " lock:"&$LOCKPARAMTEMP&" " " "
  replacetext $bot~user_command_line " lock:"&$LOCKPARAMTEMP " "

  setvar $TEMP $LOCKPARAMTEMP

  replacetext $TEMP "=" " "
  getword $TEMP $LOCKPARAM 1
  getword $TEMP $LOCKVALUE 2
  if (($LOCKPARAM = "") or ($LOCKVALUE = ""))
    setvar $switchboard~message "Issue with Lock syntax try LOCK:WHICHBUB=2*"
    gosub :switchboard~switchboard
    halt
  end
  uppercase $LOCKPARAM
end



getwordpos $bot~user_command_line $POS "a1"
if ($POS > 0)
  setvar $DROP_ARMID 1
end
getwordpos $bot~user_command_line $POS "a2"
if ($POS > 0)
  setvar $DROP_ARMID 2
end
getwordpos $bot~user_command_line $POS "a3"
if ($POS > 0)
  setvar $DROP_ARMID 3
end

getwordpos $bot~user_command_line $POS "l1"
if ($POS > 0)
  setvar $DROP_LIMP 1
end
getwordpos $bot~user_command_line $POS "l2"
if ($POS > 0)
  setvar $DROP_LIMP 2
end
getwordpos $bot~user_command_line $POS "l3"
if ($POS > 0)
  setvar $DROP_LIMP 3
end



setvar $LSDSTRING ""
if (($DROP_ARMID > 0) and ($DROP_LIMP > 0))
  setvar $DROPING_MINES 3
  setvar $LSDSTRING "0@0@0@0@0@N@M@M@0@N@0@0@N@0@0@0@0@0@0@0"
elseif ($DROP_ARMID > 0)

  setvar $DROPING_MINES 2
  setvar $LSDSTRING "0@0@0@0@0@N@N@M@0@N@0@0@N@0@0@0@0@0@0@0"
elseif ($DROP_LIMP > 0)

  setvar $DROPING_MINES 1
  setvar $LSDSTRING "0@0@0@0@0@N@M@N@0@N@0@0@N@0@0@0@0@0@0@0"
else
  setvar $DROPING_MINES 0
end

setvar $ALLLIMPS 0
setvar $ALLARMIDS 0

getwordpos $bot~user_command_line $POS "filter"
if ($POS > 0)
  setvar $FILTER_DENSITY 1
  readtoarray $bot~limp_file $ALLLIMPS
  readtoarray $bot~armid_file $ALLARMIDS
end


setvar $UPDATE_PORT FALSE
getwordpos $bot~user_command_line $POS "ports"
if ($POS > 0)
  setvar $UPDATE_PORT TRUE
end

setvar $HOLO FALSE
getwordpos $bot~user_command_line $POS "holo"
if ($POS > 0)
  setvar $HOLO TRUE
end

setvar $DROP_TWENTY 0
getwordpos $bot~user_command_line $POS "twenty"
if ($POS > 0)
  setvar $DROP_TWENTY 1
end

setvar $TWARP_SAFETY 0
getwordpos $bot~user_command_line $POS "safe"
if ($POS > 0)
  setvar $TWARP_SAFETY 1
end

getwordpos $bot~user_command_line $POS "paranoid"
if ($POS > 0)
  setvar $TWARP_SAFETY 2
end

setvar $TRACKER FALSE
getwordpos $bot~user_command_line $POS "trade"
if ($POS > 0)
  setvar $TRACKER TRUE
end

setvar $NEXTREQUIRESREPORT 0
getwordpos $bot~user_command_line $POS "nextreport"
if ($POS > 0)
  setvar $NEXTREQUIRESREPORT 1
end

setvar $RESTOCK 0
getwordpos $bot~user_command_line $POS "restock"
if ($POS > 0)
  setvar $RESTOCK 1
end


getwordpos $bot~user_command_line $POS "resume"
if ($POS > 0)
  setvar $R 11
  while ($R <= SECTORS)
    getsectorparameter $R "LSCHK" $LSCHK
    if ($LSCHK = TRUE)
      setvar $CHKD[$R] 1
    else
      setvar $CHKD[$R] 0
    end
    add $R 1
  end

else
  setvar $R 11
  while ($R <= SECTORS)
    setsectorparameter $R "LSCHK" FALSE
    add $R 1
  end
end


setvar $IGNOREA 0
getwordpos $bot~user_command_line $POS "ignorea"
if ($POS > 0)
  setvar $IGNOREA 1
end

if ($FILTER_DENSITY = 1)
  gosub :GETPERSONALPLANETS
end

goto :LETS_GET_IT_ON
:LETS_GET_IT_ON

gettime $STAMP "t d/m/yy"
if ($TRACKER)
  setvar $MCICD 0
  setarray $MCIC SECTORS

  setvar $M 11
  while ($M <= SECTORS)
    getsectorparameter $M "EQUIPMENT-" $MTEST
    isnumber $TST $MTEST
    if ($TST)

      setvar $MCIC[$M] TRUE
      add $RESULTS 1
      add $MCICD 1
    end
    add $M 1
  end


else
  if ($player~equipment_holds > 0)
    send "   j   y   "
  end
end

write $LOG_FNAME "-------------------------{ "&$STAMP&" }-------------------------"
echo "***"
if ($UPDATE_FIGS)

  gosub :BUILD_FIG_LIST
end


setvar $IDX 1
while ($IDX <= SECTORS)
  getsectorparameter $IDX "FIGSEC" $FLAG
  isnumber $TST $FLAG
  if ($TST <> 0)
    if ($FLAG <> 0)
      add $DEP_FIGS 1
    end
  else
    setsectorparameter $IDX "FIGSEC" FALSE
  end
  add $IDX 1
end

if ($DEP_FIGS = 0)
  echo $TAGLINEC&" "&ANSI_8&"<"&ANSI_15&"No Deployed Fighter Data Found"&ANSI_8&">*"
  halt
end
echo $TAGLINEC&" "&ANSI_8&"<"&ANSI_15&"Deployed Fighters "&ANSI_14&" : "&ANSI_15&$DEP_FIGS&ANSI_8&">*"


if ($UPDATE_LIMPS)
  echo $TAGLINEC&" "&ANSI_8&"<"&ANSI_15&"ReFreshing Limpet Data"&ANSI_8&">*"
  gosub :BUILD_LIMP_LIST
else
  echo $TAGLINEC&" "&ANSI_8&"<"&ANSI_15&"Reading Limps"&ANSI_8&">*"
  setvar $IDX 1
  while ($IDX <= SECTORS)
    getsectorparameter $IDX "LIMPSEC" $FLAG
    isnumber $TST $FLAG
    if ($TST <> 0)
      if ($FLAG > 0)
        setvar $LIMPS[$IDX] 1
        add $DEP_LIMP 1
      end
    end
    add $IDX 1
  end
end

window "STATUS" 500 245 " "&$TAGLINE&" v"&$VERSION




send " C ;UYQ "
waitfor "Max Figs Per Attack:"
getword CURRENTLINE $MAXFIGATTACK 5
striptext $MAXFIGATTACK ","
isnumber $TST $MAXFIGATTACK
if ($TST = 0)
  setvar $MAXFIGATTACK 9999
end
:PASSGRID_MAIN_LOOP

gosub :player~quikstats
if ($UNLIM = FALSE)
  if ($player~turns <= $TURN_LIMIT)
    goto :PASSGRID_MAIN_DONE
  end
end
:TO_THE_TOP
setvar $ANON_PTR 1
settextlinetrigger TURNSGONE :TURNSGONE "Do you want instructions (Y/N) [N]?"

send "SZND*"
waiton "Relative Density Scan"
killalltriggers
settextlinetrigger 1 :GETWARP "Sector "
settexttrigger 2 :GOTWARPINFO "Command [TL="
pause
:GETWARP
getword CURRENTLINE $ANM 13
gettext CURRENTLINE $TEMP "Warps :" "NavHaz :"
striptext $TEMP " "
striptext $TEMP ","

setvar $DENS[$ANON_PTR] $TEMP
setvar $ANOM[$ANON_PTR] $ANM
add $ANON_PTR 1
settextlinetrigger 1 :GETWARP "Sector "
pause
:GOTWARPINFO
killalltriggers

if ($TRACKER)
  gosub :HAGGEL_CHECKER
elseif (($player~ore_holds < $player~total_holds) and ($player~twarp_type <> "No"))

  if ((PORT.CLASS[$player~current_sector] = 3) or (PORT.CLASS[$player~current_sector] = 4) or (PORT.CLASS[$player~current_sector] = 5) or (PORT.CLASS[$player~current_sector] = 7))

    send "P T ** 0* 0* "
  end
end

if ($RESTOCK = 1)
  if ($player~credits < 100000)
    send "'["&$TAGLINEB&"] Restocking halted as credits low*"
    setvar $RESTOCK 0
  end

  if (($player~ore_holds = $player~total_holds) and ($player~twarp_type <> "No"))

    setvar $DORESTOCK 0
    if (($DROP_ARMID > 0) and ($DROP_LIMP > 0))
      if (($player~armids < 4) or ($player~limpets < 4))
        setvar $DORESTOCK 1
      end
    elseif ($DROP_ARMID > 0)
      if ($player~armids < 4)
        setvar $DORESTOCK 1
      end
    elseif ($DROP_LIMP > 0)
      if ($player~limpets < 4)
        setvar $DORESTOCK 1
      end
    end

    if ($DORESTOCK = 1)
      setvar $bot~command "lsd"
      setvar $bot~user_command_line $LSDSTRING
      setvar $bot~parm1 $LSDSTRING

      savevar $bot~parm1

      savevar $bot~command
      savevar $bot~user_command_line
      load "scripts\"&$bot~mombot_directory&"\modes\resource\lsd.cts"
      seteventtrigger MOVEENDED :MOVEENDED "SCRIPT STOPPED" "scripts\"&$bot~mombot_directory&"\modes\resource\lsd.cts"
      pause
      :MOVEENDED
      killalltriggers
      gosub :player~quikstats
      gosub :RESETMINESAFTERRESTOCK
    end
  end
end

setarray $ADJ_TARGETS SECTOR.WARPCOUNT[$player~current_sector]
setarray $FILTERED_DENSITY SECTOR.WARPCOUNT[$player~current_sector]

setvar $HOLOREQUIRED 0
setvar $FIRSTFILTER 1
:REFILTER


setvar $I 1
while ($I <= SECTOR.WARPCOUNT[$player~current_sector])
  setvar $ADJ SECTOR.WARPS[$player~current_sector][$I]
  setvar $CURRENTDENSITY SECTOR.DENSITY[$ADJ]
  if ($FILTER_DENSITY = 1)
    if ($planet~planetsinsectors[$adj] > 0)
      subtract $CURRENTDENSITY (500 * $planet~planetsinsectors[$adj])
    end
    if ($ALLLIMPS[$ADJ] > 0)
      subtract $CURRENTDENSITY (2 * $ALLLIMPS[$ADJ])
      setvar $ANOM[$I] "No"
    end
    if ($ALLARMIDS[$ADJ] > 0)
      subtract $CURRENTDENSITY (10 * $ALLARMIDS[$ADJ])
    end
  end

  if ($IGNOREA = 1) or (($IGNORE <> "") and ($IGNORE <> 0))
    if ($FIRSTFILTER = 1)
      getsectorparameter $ADJ "FIGSEC" $FLAG
      isnumber $TST $FLAG
      if ($TST = 0)
        setvar $FLAG 0
        setsectorparameter $ADJ "FIGSEC" FALSE
      end
      if (($FLAG = 0) and (($CURRENTDENSITY <> 0) and ($CURRENTDENSITY <> 100)))

        setvar $HOLOREQUIRED 1
      end
    else
      setvar $FIGSOWNER SECTOR.FIGS.OWNER[$ADJ]
      lowercase $FIGSOWNER
      getwordpos $FIGSOWNER $WHEREOWNER "belong to"
      getwordpos $FIGSOWNER $WHEREOWNERCORP "belong to corp#"&$IGNORE
      getwordpos $FIGSOWNER $WHEREOWNERPLAYER "belong to "&$IGNORE
      if ($WHEREOWNER = 0)
        if (SECTOR.FIGS.QUANTITY[$ADJ] < $player~fighters)
          subtract $CURRENTDENSITY (SECTOR.FIGS.QUANTITY[$ADJ] * 5)
        end
      elseif (($WHEREOWNERCORP > 0) or ($WHEREOWNERPLAYER > 0))
        if (SECTOR.FIGS.QUANTITY[$ADJ] < $player~fighters)
          subtract $CURRENTDENSITY (SECTOR.FIGS.QUANTITY[$ADJ] * 5)
        end
      end
    end
  end




  setvar $FILTERED_DENSITY[$I] $CURRENTDENSITY
  add $I 1
end

if ($HOLOREQUIRED = 1)
  setvar $FIRSTFILTER 0
  setvar $HOLOREQUIRED 0
  send "zn"
  waitfor "o you want instructions (Y/N) [N]?"
  gosub :DO_HOLO


  goto :REFILTER
end


setvar $I 1
while ($I <= SECTOR.WARPCOUNT[$player~current_sector])
  setvar $ADJ SECTOR.WARPS[$player~current_sector][$I]
  setvar $ADJ_TARGETS[$I] 10
  setvar $CURRENTDENSITY $FILTERED_DENSITY[$I]

  if (SECTOR.NAVHAZ[$ADJ] <> 0)
    setvar $FILTER 0
    setvar $FILTER (SECTOR.NAVHAZ[$ADJ] * 21)
    setvar $FILTER ($CURRENTDENSITY - $FILTER)
  else
    setvar $FILTER $CURRENTDENSITY
  end

  if ($ADJ < 10)
    setvar $BUFF "    "
  elseif ($ADJ < 100)
    setvar $BUFF "   "
  elseif ($ADJ < 1000)
    setvar $BUFF "  "
  elseif ($ADJ < 10000)
    setvar $BUFF " "
  else
    setvar $BUFF ""
  end

  getsectorparameter $ADJ "FIGSEC" $FLAG
  isnumber $TST $FLAG

  if ($TST = 0)
    setvar $FLAG 0
    setsectorparameter $ADJ "FIGSEC" FALSE
  end

  if ($SKIPPARAM <> "")
    getsectorparameter $ADJ $SKIPPARAM $SKIPCHK
    if ($SKIPCHK = "")
      setvar $SKIPCHK 0
    end

    if ($SKIPCHK <> 0)
      goto :NEXT_ADJ_PLEASE
    end
  end



  if ($LOCKPARAM <> "")
    getsectorparameter $ADJ $LOCKPARAM $LOCKCHK
    if ($LOCKCHK = "")
      setvar $LOCKCHK 0
    end

    if ($LOCKCHK <> $LOCKVALUE)
      goto :NEXT_ADJ_PLEASE
    end
  end





  if (($CURRENTDENSITY > 200) and ($FLAG = 0))
    setvar $STRMSG "Sect: "&$BUFF&$ADJ&" Den: "&$CURRENTDENSITY&" Haz: "&SECTOR.NAVHAZ[$ADJ]&"% Filtered: "&$FILTER
    write $LOG_FNAME $STRMSG
    add $LOG_EVENT 1
    setvar $LOG_TEXT $STRMSG
    gosub :MOVE_DOWN
    send "'["&$TAGLINEB&"] "&$STRMSG&"*"
    waitfor "Message sent on sub-space channel"
  elseif (SECTOR.NAVHAZ[$ADJ] <> 0)
    setvar $STRMSG "NavHaz in Sect: "&$BUFF&$ADJ&" Den: "&$CURRENTDENSITY&" Haz: "&SECTOR.NAVHAZ[$ADJ]&"% Filtered: "&$FILTER
    write $LOG_FNAME $STRMSG
    add $LOG_EVENT 1
    setvar $LOG_TEXT $STRMSG
    gosub :MOVE_DOWN
    send "'["&$TAGLINEB&"] "&$STRMSG&"*"
    waitfor "Message sent on sub-space channel"
  end
  if ((($CURRENTDENSITY = 0) or ($CURRENTDENSITY = 5)) and ($ANOM[$I] = "Yes"))
    setvar $STRMSG "Cloaked Ship, Sect: "&$BUFF&$ADJ&" Den: "&$CURRENTDENSITY&" Haz: "&SECTOR.NAVHAZ[$ADJ]&"% Filtered: "&$FILTER
    write $LOG_FNAME $STRMSG
    add $LOG_EVENT 1
    setvar $LOG_TEXT $STRMSG
    gosub :MOVE_DOWN
    send "'["&$TAGLINEB&"] "&$STRMSG&"*"
    waitfor "Message sent on sub-space channel"
  end

  if (($CURRENTDENSITY = 40) or ($CURRENTDENSITY = 45) or ($CURRENTDENSITY = 140) or ($CURRENTDENSITY = 145))
    setvar $STRMSG "Possible Trader, Sect: "&$BUFF&$ADJ&" Den: "&$CURRENTDENSITY&" Haz: "&SECTOR.NAVHAZ[$ADJ]&"% Filtered: "&$FILTER
    write $LOG_FNAME $STRMSG
    add $LOG_EVENT 1
    setvar $LOG_TEXT $STRMSG
    gosub :MOVE_DOWN
    send "'["&$TAGLINEB&"] "&$STRMSG&"*"
    waitfor "Message sent on sub-space channel"
  end




  if (($ANOM[$I] = "Yes") and ($LIMPS[$ADJ] = 0))
    goto :NEXT_ADJ_PLEASE
  end


  if ($FLAG = 0)
    if (($CURRENTDENSITY = 0) or ($CURRENTDENSITY = 100))
      if (SECTOR.NAVHAZ[$ADJ] = 0)
        if (SECTOR.EXPLORED[$ADJ] <> "YES")
          if ($DENS[$I] > 1)
            setvar $ADJ_TARGETS[$I] 1
            goto :NEXT_ADJ_PLEASE
          end
        end
      end
    end

    if (($CURRENTDENSITY = 0) or ($CURRENTDENSITY = 100))
      if (SECTOR.NAVHAZ[$ADJ] = 0)
        if (SECTOR.EXPLORED[$ADJ] = "YES")
          if ($DENS[$I] > 1)
            setvar $ADJ_TARGETS[$I] 2
            goto :NEXT_ADJ_PLEASE
          end
        end
      end
    end
    if (($CURRENTDENSITY = 0) or ($CURRENTDENSITY = 100))
      if (SECTOR.NAVHAZ[$ADJ] = 0)
        if (SECTOR.EXPLORED[$ADJ] <> "YES")
          if ($DENS[$I] >= 1)
            setvar $ADJ_TARGETS[$I] 3
            goto :NEXT_ADJ_PLEASE
          end
        end
      end
    end
    if (($CURRENTDENSITY = 0) or ($CURRENTDENSITY = 100))
      if (SECTOR.NAVHAZ[$ADJ] = 0)
        if (SECTOR.EXPLORED[$ADJ] = "YES")
          if ($DENS[$I] >= 1)
            setvar $ADJ_TARGETS[$I] 4
            goto :NEXT_ADJ_PLEASE
          end
        end
      end
    end
  end

  if (($CURRENTDENSITY = 105) or ($CURRENTDENSITY = 5))
    if (SECTOR.NAVHAZ[$ADJ] = 0)
      if (SECTOR.EXPLORED[$ADJ] <> "YES")
        if ($FLAG <> 0)
          if ($DENS[$I] > 1)
            setvar $ADJ_TARGETS[$I] 5
            goto :NEXT_ADJ_PLEASE
          end
        end
      end
    end
  end





  if ($player~twarp_type = "No")

    if (($CURRENTDENSITY = 105) or ($CURRENTDENSITY = 5))

      if (SECTOR.WARPCOUNT[$ADJ] >= 5)
        if (SECTOR.NAVHAZ[$ADJ] = 0)
          if ($FLAG = 1)
            if ($DENS[$I] >= 1)
              if ($CHKD[$ADJ] = 0)
                setvar $ADJ_TARGETS[$I] 6
                goto :NEXT_ADJ_PLEASE
              end
            end
          end
        end
      end
    end
    if (($CURRENTDENSITY = 105) or ($CURRENTDENSITY = 5))

      if (SECTOR.WARPCOUNT[$ADJ] > 1)
        if (SECTOR.NAVHAZ[$ADJ] = 0)
          if ($FLAG = 1)
            if ($DENS[$I] >= 1)
              if ($CHKD[$ADJ] = 0)
                setvar $ADJ_TARGETS[$I] 6
                goto :NEXT_ADJ_PLEASE
              end
            end
          end
        end
      end
    end
  end
  :NEXT_ADJ_PLEASE
  add $I 1
end

setvar $IDX 1
setvar $TARGET 10
setvar $TARGET_IDX 0

while ($IDX <= SECTOR.WARPCOUNT[$player~current_sector])
  if (($ADJ_TARGETS[$IDX] < $TARGET) and ($TARGET <> 0))
    setvar $TARGET $ADJ_TARGETS[$IDX]
    setvar $TARGET_IDX $IDX
  end
  add $IDX 1
end

if ($TARGET_IDX <> 0)
  setvar $TARGET SECTOR.WARPS[$player~current_sector][$TARGET_IDX]
  if (SECTOR.DENSITY[$TARGET] >= 100)
    send " c r"&$TARGET&"*q"
    settextlinetrigger NODATA1 :NODATA "You have never visted sector"
    settextlinetrigger NODATA2 :NODATA "I have no information about a port in that sector"
    settextlinetrigger YADATA1 :YADATA "Items     Status  Trading % of max OnBoard"
    settextlinetrigger YADATA2 :YADATA "A  Cargo holds     :"
    pause
    :NODATA
    killalltriggers
    if ($HOLO)
      gosub :DO_HOLO
      gosub :DISPLAY_HOLO
      waiton "Command [TL="
      if (SECTOR.FIGS.QUANTITY[$TARGET] <> 0)
        if ((SECTOR.FIGS.OWNER[$TARGET] <> "belong to your Corp") and (SECTOR.FIGS.OWNER[$TARGET] <> "yours"))

          setvar $IGNORE $TARGET
          setvar $IDX 1
          setvar $TARGET 10
          setvar $TARGET_IDX 0
          while ($IDX <= SECTOR.WARPCOUNT[$player~current_sector])
            if (($ADJ_TARGETS[$IDX] < $TARGET) and (($TARGET <> 0) and (SECTOR.WARPS[$player~current_sector][$IDX] <> $IGNORE)))
              setvar $TARGET $ADJ_TARGETS[$IDX]
              setvar $TARGET_IDX $IDX
            end
            add $IDX 1
          end
          if ($TARGET_IDX <> 0)
            setvar $TARGET SECTOR.WARPS[$player~current_sector][$TARGET_IDX]
          else
            goto :NO_TARGET
          end
        end
      end
    end
    :YADATA
    killalltriggers
  end
  goto :NEXT_TARGET
end
:NO_TARGET


if ($player~twarp_type <> "No")

  getnearestwarps $WARPARRAY $player~current_sector
  getrnd $W 5 10
  while ($W <= $WARPARRAY)
    setvar $FOCUS $WARPARRAY[$W]
    if ($FOCUS <> $player~current_sector)
      getsectorparameter $FOCUS "FIGSEC" $FLAG
      isnumber $TST $FLAG
      if ($TST = 0)
        setvar $FLAG 0
        setsectorparameter $FOCUS "FIGSEC" FALSE
      end
      if ($FLAG <> FALSE)
        if ($TWARP_SAFETY = 1)
          getsectorparameter $FOCUS "LIMPSEC" $FLAG
          isnumber $TST $FLAG
          if ($TST = 0)
            setvar $FLAG 0
            setsectorparameter $FOCUS "LIMPSEC" FALSE
          end
        elseif ($TWARP_SAFETY = 2)


          getsectorparameter $FOCUS "LIMPSEC" $FLAG1
          isnumber $TST1 $FLAG1
          if ($TST1 = 0)
            setvar $FLAG1 0
            setsectorparameter $FOCUS "LIMPSEC" FALSE
          end

          getsectorparameter $FOCUS "MINESEC" $FLAG2
          isnumber $TST2 $FLAG2
          if ($TST2 = 0)
            setvar $FLAG2 0
            setsectorparameter $FOCUS "MINESEC" FALSE
          end

          if (($FLAG1 = 0) or ($FLAG2 = 0))
            setvar $FLAG 0
          else
            setvar $FLAG 1
          end
        end
      end

      if ($FLAG <> 0)
        if (SECTOR.WARPCOUNT[$FOCUS] > 1)
          setvar $W_I 1
          while ($W_I <= SECTOR.WARPCOUNT[$FOCUS])
            setvar $W_ADJ SECTOR.WARPS[$FOCUS][$W_I]
            getsectorparameter $W_ADJ "FIGSEC" $FLAG
            isnumber $TST $FLAG

            if ($TST = 0)
              setvar $FLAG 0
              setsectorparameter $W_ADJ "FIGSEC" FALSE
            end

            setvar $SKIPWARP 0
            if ($SKIPPARAM <> "")
              getsectorparameter $W_ADJ $SKIPPARAM $SKIPCHK
              if ($SKIPCHK = "")
                setvar $SKIPCHK 0
              end
              if ($SKIPCHK <> 0)
                setvar $SKIPWARP 1
              end
            end

            if ($LOCKPARAM <> "")
              getsectorparameter $ADJ $LOCKPARAM $LOCKCHK
              if ($LOCKCHK = "")
                setvar $LOCKCHK 0
              end
              if ($LOCKCHK <> $LOCKVALUE)
                setvar $SKIPWARP 1
              end
            end

            if (($FLAG = 0) and (($CHKD[$W_ADJ] <> 1) and ($SKIPWARP = 0)))
              setvar $CHKD[$W_ADJ] 1
              if ($NEXTREQUIRESREPORT = 1)

                setvar $PORTOK 0
                if (PORT.EXISTS[$W_ADJ] = 1)
                  send "cr" $W_ADJ "*q"
                  waitfor "Computer activate"
                  settextlinetrigger PORTEXISTS :PORTEXISTS "Commerce report for"
                  settextlinetrigger PORTEXISTSNO :PORTEXISTSNO "I have no information about a port in that sector"
                  settextlinetrigger PORTEXISTSNO2 :PORTEXISTSNO2 "u have never visted sector"
                  pause
                  :PORTEXISTS
                  setvar $PORTOK 1
                  :PORTEXISTSNO
                  :PORTEXISTSNO2

                  killtrigger PORTEXISTSNO
                  killtrigger PORTEXISTSNO2
                  killtrigger PORTEXISTS
                end



                if ($PORTOK = 1)
                  goto :WE_GOT_GAME
                end
              else

                goto :WE_GOT_GAME
              end
            end


            add $W_I 1
          end
        end
      end
    end
    add $W 1
  end
  :WE_DONE


  echo "**"&$TAGLINEC&" "&" No Target To Find. Try updating CIM***"
  halt
  :WE_GOT_GAME


  if ($HOLO)
    setvar $CX 1
    setvar $CN 0
    while (SECTOR.WARPS[$player~current_sector][$CX] <> 0)
      setvar $ADJ SECTOR.WARPS[$player~current_sector][$CX]
      if ((SECTOR.EXPLORED[$ADJ] = "NO") or (SECTOR.EXPLORED[$ADJ] = "CALC"))
        add $CN 1
      end
      add $CX 1
    end
    if ($CN > 2)
      gosub :DO_HOLO
      gosub :DISPLAY_HOLO
    end
  end
  setvar $ENGAGESTRING "Y"
  send " M"&$FOCUS&"*Y"
  settextlinetrigger SECTOR__GOOD :SECTOR__GOOD "Locating beam pinpointed, TransWarp"
  settextlinetrigger SECTOR__HERE :SECTOR__GOODNAV "<Set NavPoint>"
  settextlinetrigger SECTOR__BAD :SECTOR__BAD "No locating beam found"
  settexttrigger SECTOR__FAR :SECTOR__FAR "You do not have enough Fuel Ore to make the jump."
  pause
  :SECTOR__BAD
  killalltriggers
  goto :WE_DONE
  :SECTOR__FAR
  killalltriggers
  getnearestwarps $WARPARRAY $player~current_sector
  setvar $C 1
  while ($C <= $WARPARRAY)
    setvar $FOCUS $WARPARRAY[$C]
    if ((PORT.CLASS[$FOCUS] = 3) or (PORT.CLASS[$FOCUS] = 4) or (PORT.CLASS[$FOCUS] = 5) or (PORT.CLASS[$FOCUS] = 7))
      getsectorparameter $FOCUS "FIGSEC" $FLAG
      isnumber $TST $FLAG
      if ($TST = 0)
        setvar $FLAG 0
        setsectorparameter $FOCUS "FIGSEC" FALSE
      end
      if ($FLAG = 1)
        setvar $DESTINATION $FOCUS
        gosub :GETCOURSE
        if ($COURSELENGTH <> 0)
          setvar $J 2
          setvar $RESULT ""

          while ($J <= $COURSELENGTH)
            getsectorparameter $COURSE[$J] "FIGSEC" $FLAG
            isnumber $TST $FLAG
            if ($TST = 0)
              setvar $FLAG 0
              setsectorparameter $COURSE[$J] "FIGSEC" FALSE
            end
            if (($FLAG = 0) and ($COURSE[$J] <> $player~current_sector))
              goto :NEXT_SXX_PORT
            end
            setvar $RESULT $RESULT&"m"&$COURSE[$J]&"* "
            if (($COURSE[$J] > 10) and ($COURSE[$J] <> STARDOCK))
              setvar $RESULT $RESULT&" Z  A  "&$MAXFIGATTACK&"*  *  "
            end

            if (($COURSE[$J] > 10) and (($COURSE[$J] <> STARDOCK) and ($J > 2)))
              getsectorparameter $COURSE[$J] "FIGSEC" $FLAG
              isnumber $TST $FLAG
              if ($TST = 0)
                setvar $FLAG 0
                setsectorparameter $COURSE[$J] "FIGSEC" FALSE
              end
              if ($FLAG = 0)
                setvar $RESULT $RESULT&" F  Z  1 * Z  C  D  *  "
                setsectorparameter $COURSE[$J] "FIGSEC" TRUE
              end
            end
            add $J 1
          end
          waitfor "Command ["

          if ($TRACKER)
            send $RESULT&"  **  "
            gosub :player~quikstats
            gosub :HAGGEL_CHECKER
          else
            send $RESULT&"  **    P   T   *   *   *   *   "
          end

          gosub :player~quikstats
          if (($player~total_holds <> $player~ore_holds) and ($TRACKER = 0))
            if ($player~credits < 10000)
              echo "**"&$TAGLINEC&" "&" Appear To Be Out of Funds for ORE purchase.**"
            elseif (($UNLIM = FALSE) and (CURRENTTURNS < 1))
              echo "**"&$TAGLINEC&" "&" Appear To Be Out Turns. Photon'd Maybe??**"
            else
              echo "**"&$TAGLINEC&" "&" Not Enough ORE to continue.**"
            end
            halt
          end
          if ($TRACKER and ($player~ore_holds < ($player~total_holds - $EQU_MIN)))
            if ($player~credits < 10000)
              echo "**"&$TAGLINEC&" "&" Appear To Be Out of Funds for ORE purchase.**"
            elseif (($UNLIM = FALSE) and (CURRENTTURNS < 1))
              echo "**"&$TAGLINEC&" "&" Appear To Be Out Turns. Photon'd Maybe??**"
            else
              echo "**"&$TAGLINEC&" "&" Not Enough ORE to continue.**"
            end
            halt
          end
          if ($player~credits < 10000)
            echo "**"&$TAGLINEC&" "&" Too Few Credits to continue.**"
            halt
          end
          goto :TO_THE_TOP
        end
      end
    end
    :NEXT_SXX_PORT
    add $C 1
  end
  goto :WE_DONE
  :SECTOR__GOODNAV
  send "*q"
  setvar $ENGAGESTRING ""
  :SECTOR__GOOD
  killalltriggers
  setvar $DROP_STR ""
  if ($DROPING_MINES <> 0)
    if (SECTOR.WARPINCOUNT[$FOCUS] >= 3)
      if (($DROPING_MINES = 1) or ($DROPING_MINES = 3))
        if ($player~limpets > $DROP_LIMP)
          setvar $DROP_STR $DROP_STR&"H 2 Z "&$DROP_LIMP&"* C * "
        else
          if ($DROPING_MINES = 1)
            setvar $DROPING_MINES 0
          else
            setvar $DROPING_MINES 2
          end
        end
      end

      if (($DROPING_MINES = 2) or ($DROPING_MINES = 3))
        if ($player~armids > $DROP_ARMID)
          setvar $DROP_STR $DROP_STR&"H 1 Z "&$DROP_ARMID&"* C * "
        else
          if ($DROPING_MINES = 2)
            setvar $DROPING_MINES 0
          else
            setvar $DROPING_MINES 1
          end
        end
      end
    end
  end
  send $ENGAGESTRING "  *  A Z "&$MAXFIGATTACK&"998877665544332211 n  *  **   "&$DROP_STR
  gosub :player~quikstats
  goto :TO_THE_TOP
end
echo "**"&$TAGLINEC&" "&" Walled In (No Twarp Available)***"
halt
:NEXT_TARGET



setvar $FIGSTODROP 1
setvar $DENSITY_TRICK FALSE
while (SECTOR.DENSITY[$TARGET] = 0)
  if ($DROP_TWENTY = 1)
    setvar $DENSITY_TRICK TRUE
    setvar $FIGSTODROP 20
  end
end
send "  m "&$TARGET&" *  z  a  "&$MAXFIGATTACK&"99887766554433221100  n  *  dz  n  f  z  " $FIGSTODROP "*  z  c  d  *  "
settextlinetrigger U_TORPED :HELP_ME "Your ship was hit by a Photon and has been disabled."
settextlinetrigger NO_TURNS :HELP_ME "You don't have enough turns left."
settextlinetrigger IG_HOLD1 :HELP_ME "You attempt to retreat but are held fast by an Interdictor Generator."
settextlinetrigger IG_HOLD2 :HELP_ME "An Interdictor Generator in this sector holds you fast!"
settextlinetrigger QUASAR_B :HELP_ME "Quasar Blast!"
waiton ":["&$TARGET&"] (?=Help)"
goto :HELP_ME_JMP
:HELP_ME
killalltriggers
getword CURRENTLINE $SPOOFY 1
if (($SPOOFY <> "Your") and (($SPOOFY <> "You") and (($SPOOFY <> "An") and ($SPOOFY <> "Quasar"))))
  goto :HELP_ME_JMP
end
stop "_CK_CALLSAVEME"
:HELP_ME_JMP






add $DEP_FIGS 1
add $DEP_NEW 1
setsectorparameter $TARGET "FIGSEC" TRUE
setvar $CHKD[$TARGET] 1

setvar $DROP_STR ""

if ($DENSITY_TRICK <> TRUE)
  if ($DROPING_MINES <> 0)
    if (SECTOR.WARPINCOUNT[$TARGET] >= 3)
      if (($DROPING_MINES = 1) or ($DROPING_MINES = 3))
        if ($player~limpets > $DROP_LIMP)
          setvar $DROP_STR $DROP_STR&"H 2 Z "&$DROP_LIMP&"* C * "
        else
          if ($DROPING_MINES = 1)
            setvar $DROPING_MINES 0
          else
            setvar $DROPING_MINES 2
          end
        end
      end

      if (($DROPING_MINES = 2) or ($DROPING_MINES = 3))
        if ($player~armids > $DROP_ARMID)
          setvar $DROP_STR $DROP_STR&"H 1 Z "&$DROP_ARMID&"* C * "
        else
          if ($DROPING_MINES = 2)
            setvar $DROPING_MINES 0
          else
            setvar $DROPING_MINES 1
          end
        end
      end
    end
  end
end
if ($DROP_STR <> "")
  send $DROP_STR&"  j  *"
  waiton "Are you sure you want to jettison all cargo?"
end
gosub :player~quikstats

if ($player~current_prompt <> "Command")
  echo "**"&$TAGLINEC&" "&"Wrong Prompt After Sector Hit.***"
  halt
end

if ($TRACKER)
  gosub :HAGGEL_CHECKER
end

if ($player~current_prompt <> "Command")
  send " r *  *  p d 0* 0* 0* * *** * c q q q q q z 2 2 c q * z * *** * * "
  gosub :player~quikstats
  if ($player~current_prompt = "Command")
    load "_ck_callsaveme.cts"
    halt
  end
  echo "**"&$TAGLINEC&" "&"Hmmm..  I seem to be stuck.***"
  halt
end


gosub :UPDATESTATUS_WINDOW

while ($REPORT_RYLOS and (RYLOS > 1))
  send "'["&$TAGLINEB&"] Class 0 RYLOS Spotted In Sector: "&RYLOS&"*"
  waitfor "Message sent on sub-space channel"
  setvar $REPORT_RYLOS FALSE
end
if ($REPORT_ALPHA and (ALPHACENTAURI > 1))
  send "'["&$TAGLINEB&"] Class 0 ALPHACENTAURI Spotted In Sector: "&ALPHACENTAURI&"*"
  waitfor "Message sent on sub-space channel"
  setvar $REPORT_ALPHA FALSE
end

if ($UPDATE_PORT and PORT.EXISTS[$TARGET])
  send "CR*Q"
  waitfor "<Computer deactivated>"
end

if ($player~fighters <= 10)
  echo "**"&$TAGLINEC&" "&"Fighter Level is Critically Low (Less Than 10)**"
  halt
end
goto :PASSGRID_MAIN_LOOP
:PASSGRID_MAIN_DONE

if ($UNLIM = 0)
  if (CURRENTTURNS <= $TURN_LIMIT)
    send "'["&$TAGLINEB&"] Turn Limit Reached, Halting*"
  end
else
  send "'["&$TAGLINEB&"] Nothing To Do*"
end

halt
:TURNSGONE

killalltriggers
send "   *   *    *   /"
waiton #179&"Turns"
gettext CURRENTLINE $LOCAL "Sect" #179&"Turns"
striptext $LOCAL " "
send "'"
waitfor "Sub-space radio ("
send $LOCAL&"=saveme*"
waitfor "Message sent on sub-space channel"
send "F  Z  1*  Z  C  D  *  "
setdelaytrigger NOHELPCOMMING :NOHELPCOMMING 4000
settextlinetrigger HELPCAME :HELPCAME "Saveme script activated - "
pause
:NOHELPCOMMING
killalltriggers
send "'["&$TAGLINEB&"] No Help Came.*"
halt
:HELPCAME
killalltriggers
gettext CURRENTLINE $planet~planet "Planet" "to"
striptext $planet~planet " "
send "L Z"&#8&$planet~planet&"*  J  C  *  "
halt
:BUILD_FIG_LIST



killalltriggers
send "'Scanning Deployed Fighters...*G"
setvar $IDX 1
while ($IDX <= SECTORS)
  setsectorparameter $IDX "FIGSEC" FALSE
  add $IDX 1
end
killalltriggers
waitfor "==========================================================="
settextlinetrigger FIGLINE1 :ADDINFIGC " Corp "
settextlinetrigger FIGLINE2 :ADDINFIGP " Personal "
settextlinetrigger LSTBOTTOM :LSTBOTTOM " Total "
settextlinetrigger LSTNONE :LSTBOTTOM "No fighters deployed"
pause
:ADDINFIGP
getword CURRENTLINE $SECTOR 1
setsectorparameter $SECTOR "FIGSEC" TRUE
add $DEP_FIGS 1
settextlinetrigger FIGLINE2 :ADDINFIGP " Personal "
pause
:ADDINFIGC
getword CURRENTLINE $SECTOR 1
setsectorparameter $SECTOR "FIGSEC" TRUE
add $DEP_FIGS 1
settextlinetrigger FIGLINE1 :ADDINFIGC " Corp "
pause
:LSTBOTTOM
killalltriggers

return
:BUILD_LIMP_LIST

killalltriggers
setarray $LIMPS SECTORS

setvar $IDX 1
while ($IDX <= SECTORS)
  setsectorparameter $IDX "LIMPSEC" 0
  add $IDX 1
end

send "'Scanning Deployed Limpets...*k2"
waitfor "===================================="
settextlinetrigger LIMPLINE1 :ADDINLIMPC " Corporate"
settextlinetrigger LIMPLINE2 :ADDINLIMPP " Personal "
settextlinetrigger LSTBOTTOM :LIMPLSTBOTTOM "Activated  Limpet  Scan"
settextlinetrigger LSTNONE :LIMPLSTBOTTOM "No Limpet mines deployed"
pause
:ADDINLIMPC
getword CURRENTLINE $SECTOR 1
setsectorparameter $SECTOR "LIMPSEC" TRUE
add $DEP_LIMP 1
setvar $LIMPS[$SECTOR] TRUE
settextlinetrigger LIMPLINE1 :ADDINLIMPC " Corporate"
pause
:ADDINLIMPP
getword CURRENTLINE $SECTOR 1
setsectorparameter $SECTOR "LIMPSEC" TRUE
add $DEP_LIMP 1
setvar $LIMPS[$SECTOR] TRUE
settextlinetrigger LIMPLINE2 :ADDINLIMPP " Personal "
pause
:LIMPLSTBOTTOM
killalltriggers

return
:UPDATESTATUS_WINDOW

setvar $WINDOW_TXT ""

setvar $WINDOW_TXT $WINDOW_TXT&" Sector    : "&$player~current_sector&"*"
if ($UNLIM)
  setvar $WINDOW_TXT $WINDOW_TXT&" Turns     : Unlimited*"
else
  setvar $CASHAMOUNT CURRENTTURNS
  gosub :COMMASIZE
  setvar $WINDOW_TXT $WINDOW_TXT&" Turns     : "&$CASHAMOUNT
  setvar $CASHAMOUNT $TURN_LIMIT
  gosub :COMMASIZE
  setvar $WINDOW_TXT $WINDOW_TXT&" (Turn Limit "&$CASHAMOUNT&")*"
end

setvar $CASHAMOUNT $player~credits
gosub :COMMASIZE
setvar $WINDOW_TXT $WINDOW_TXT&" Credits   : $"&$CASHAMOUNT&"*"

setvar $CASHAMOUNT $player~fighters
gosub :COMMASIZE
setvar $WINDOW_TXT $WINDOW_TXT&" Fighters  : "&$CASHAMOUNT&"*"

setvar $CASHAMOUNT $DEP_FIGS
gosub :COMMASIZE
setvar $CASHAMOUNT1 $CASHAMOUNT
setvar $CASHAMOUNT SECTORS
gosub :COMMASIZE
setvar $WINDOW_TXT $WINDOW_TXT&" Grid      : "&$CASHAMOUNT1&" of "&$CASHAMOUNT&"*"

setvar $CASHAMOUNT $DEP_NEW
gosub :COMMASIZE
setvar $WINDOW_TXT $WINDOW_TXT&" Gridded   : "&$CASHAMOUNT&"*"
if ($TRACKER)
  setvar $CASHAMOUNT $MCICD
  gosub :COMMASIZE
  setvar $WINDOW_TXT $WINDOW_TXT&" MCIC'd    : "&$CASHAMOUNT&" ("&$TRACK_FILE&")*"
end

setvar $WINDOW_TXT $WINDOW_TXT&"    ----------------: Log Entries :----------------*"
setvar $II 1

while ($II <= 5)
  if ($LOG_ENTRIES[$II] <> "")
    setvar $WINDOW_TXT $WINDOW_TXT&" "&$LOG_ENTRIES[$II]&"*"
  end
  add $II 1
end
setwindowcontents "STATUS" "*"&$WINDOW_TXT
setvar $WINDOW_CONTENT $WINDOW_TXT
replacetext $WINDOW_CONTENT "*" "[][]"
savevar $WINDOW_CONTENT
return
:COMMASIZE

if ($CASHAMOUNT < 1000)

elseif ($CASHAMOUNT < 1000000)
  getlength $CASHAMOUNT $LEN
  setvar $LEN ($LEN - 3)
  cuttext $CASHAMOUNT $TMP 1 $LEN
  cuttext $CASHAMOUNT $TMP1 ($LEN + 1) 999
  setvar $TMP $TMP&","&$TMP1
  setvar $CASHAMOUNT $TMP
elseif ($CASHAMOUNT <= 999999999)
  getlength $CASHAMOUNT $LEN
  setvar $LEN ($LEN - 6)
  cuttext $CASHAMOUNT $TMP 1 $LEN
  setvar $TMP $TMP&","
  cuttext $CASHAMOUNT $TMP1 ($LEN + 1) 3
  setvar $TMP $TMP&$TMP1&","
  cuttext $CASHAMOUNT $TMP1 ($LEN + 4) 999
  setvar $TMP $TMP&$TMP1
  setvar $CASHAMOUNT $TMP
end
return
:MOVE_DOWN

setvar $LOG_ENTRIES[5] $LOG_ENTRIES[4]
setvar $LOG_ENTRIES[4] $LOG_ENTRIES[3]
setvar $LOG_ENTRIES[3] $LOG_ENTRIES[2]
setvar $LOG_ENTRIES[2] $LOG_ENTRIES[1]
setvar $LOG_ENTRIES[1] $LOG_EVENT&" "&$LOG_TEXT
return
:GETCOURSE

killalltriggers
setvar $SECTORS ""
settextlinetrigger SECTORLINETRIG :SECTORSLINE " > "
send "^f*"&$DESTINATION&"*nq"
pause
:SECTORSLINE
killalltriggers
setvar $LINE CURRENTLINE
replacetext $LINE ">" " "
striptext $LINE "("
striptext $LINE ")"
setvar $LINE $LINE&" "
getwordpos $LINE $POS "So what's the point?"
getwordpos $LINE $POS2 ": ENDINTERROG"
getwordpos $LINE $POS3 "*** Error"

if (($POS > 0) or ($POS2 > 0))
  setvar $COURSELENGTH 0
  return
end
getwordpos $LINE $POS " sector "
getwordpos $LINE $POS2 "TO"
if (($POS <= 0) and ($POS2 <= 0))
  setvar $SECTORS $SECTORS&" "&$LINE
end
getwordpos $LINE $POS " "&$DESTINATION&" "
getwordpos $LINE $POS2 "("&$DESTINATION&")"
getwordpos $LINE $POS3 "TO"
if ((($POS > 0) or ($POS2 > 0)) and ($POS3 <= 0))
  goto :GOTSECTORS
end
:WAITFORNEXTCOURSELINE
settextlinetrigger SECTORLINETRIG :SECTORSLINE " > "
settextlinetrigger SECTORLINETRIG2 :SECTORSLINE " "&$DESTINATION&" "
settextlinetrigger SECTORLINETRIG3 :SECTORSLINE " "&$DESTINATION
settextlinetrigger SECTORLINETRIG4 :SECTORSLINE "("&$DESTINATION&")"
settextlinetrigger DONEPATH :SECTORSLINE "So what's the point?"
settextlinetrigger DONEPATH2 :SECTORSLINE ": ENDINTERROG"
pause
:GOTSECTORS

killalltriggers
setvar $SECTORS $SECTORS&" :::"
setvar $COURSELENGTH 0
setvar $INDEX 1
:KEEPGOING
if ($SECTORS = " FM     :::")
  return
end
getword $SECTORS $COURSE[$INDEX] $INDEX
while ($COURSE[$INDEX] <> ":::")
  add $COURSELENGTH 1
  add $INDEX 1
  getword $SECTORS $COURSE[$INDEX] $INDEX
end
return
:HAGGEL_CHECKER

killalltriggers







setvar $DOTRADE 0
if (($player~ore_holds < 75) and (PORT.BUYFUEL[$player~current_sector] = 0))
  setvar $DOTRADE 1
elseif (($player~equipment_holds < $EQU_MIN_BUY) and (PORT.BUYEQUIP[$player~current_sector] = 0))
  setvar $DOTRADE 1
elseif ((PORT.BUYEQUIP[$player~current_sector] = 1) and ($mcic[$player~current_sector] = FALSE))
  setvar $DOTRADE 1
end
if ($DOTRADE = 0)
  return
end


setvar $RESTOREHAGGLE 0
if (HAGGLE)
  setvar $RESTOREHAGGLE 1
  autohaggle "OFF"
end

setvar $EQU_NEED2BUY ($EQU_MIN - $player~equipment_holds)
setvar $ORE_NEED2BUY (($player~total_holds - $EQU_MIN) - $player~ore_holds)
if ((PORT.CLASS[$player~current_sector] = 1) or (PORT.CLASS[$player~current_sector] = 5) or (PORT.CLASS[$player~current_sector] = 6) or (PORT.CLASS[$player~current_sector] = 7) or (PORT.CLASS[$player~current_sector] = 3) or (PORT.CLASS[$player~current_sector] = 4) or (PORT.CLASS[$player~current_sector] = 2))



  setvar $TRADESTARTED 0
  settexttrigger NOPORT :NOPORT "Corp Menu"
  send "pt"
  waiton "<Port>"
  settexttrigger NOFUEL :NOFUEL "How many holds of Fuel Ore do you want to buy"
  settexttrigger NOORG :NOORG "How many holds of Organics do you want to buy"
  settexttrigger EQUP :EQUP "How many holds of Equipment do you want to sell ["
  settexttrigger BUYEQUP :BUYEQUP "How many holds of Equipment do you want to buy"
  settexttrigger NOSELL :NOSELL "You don't have anything they want"
  settexttrigger FUELSELL :FUELSELL "How many holds of Fuel Ore do you want to sell"
  settexttrigger ORGSELL :ORGSELL "How many holds of Organics do you want to sell"
  settexttrigger OFFER :OFFER "Your offer ["
  settexttrigger FINALOFFER :OFFER "Our final offer"
  settexttrigger DONE :DONE "Command [TL"
  pause
  :NOPORT
  killalltriggers
  gosub :RESTOREHAGGLE
  echo "***Hmmm.. where'd the port go?!?**"
  halt
  :DONE
  if ($TRADESTARTED = 0)
    settexttrigger DONE :DONE "Command [TL"
    pause
  end
  killalltriggers
  gosub :RESTOREHAGGLE
  return
  :NOFUEL
  setvar $TRADESTARTED 1
  if ($ORE_NEED2BUY >= 1)

    send $ORE_NEED2BUY&"*"
  else
    send "0*"
  end
  pause
  :NOORG
  setvar $TRADESTARTED 1
  send "0*"
  pause
  :EQUP
  setvar $TRADESTARTED 1
  if ($mcic[$player~current_sector] = 0)
    setvar $mcic[$player~current_sector] TRUE
    if ($player~equipment_holds > $EQU_MIN)

      send ($player~equipment_holds - $EQU_MIN)&"*"
    else
      add $MCICD 1

      send "5*"
    end
  else
    send "0*"
  end
  pause
  :BUYEQUP
  setvar $TRADESTARTED 1
  if ($EQU_NEED2BUY >= 1)

    send $EQU_NEED2BUY&"*"
  else
    send "0*"
  end
  pause
  :NOSELL
  setvar $TRADESTARTED 1
  killalltriggers
  gosub :RESTOREHAGGLE
  return
  :OFFER
  setvar $TRADESTARTED 1
  send "*"
  pause
  :FUELSELL
  setvar $TRADESTARTED 1
  if ($player~ore_holds > ($player~total_holds - $EQU_MIN))

    send ($player~ore_holds - ($player~total_holds - $EQU_MIN))&"*"
  else
    send "0*"
  end
  pause
  :ORGSELL
  setvar $TRADESTARTED 1

  send "*"
  pause
end
gosub :RESTOREHAGGLE
return
:RESTOREHAGGLE
if ($RESTOREHAGGLE = 1)
  autohaggle "ON"
  setvar $RESTOREHAGGLE 0
end
return
:DO_HOLO
setarray $HOLOOUTPUT 2000
setvar $LINE_POINTER 1
send "SzH*  "
settextlinetrigger TURNSGONE :TURNSGONE "Do you want instructions (Y/N) [N]?"
settextlinetrigger DONESCAN :DONESCAN "Warps to Sector(s) :"

waiton "Long Range Scan"
:RESET_TRIGGER
settextlinetrigger HOLO_LINE :HOLO_LINE
pause
:HOLO_LINE
setvar $HOLOOUTPUT[$LINE_POINTER] CURRENTLINE
if ($LINE_POINTER <= 2000)
  add $LINE_POINTER 1
end
goto :RESET_TRIGGER
:DONESCAN
killalltriggers
setvar $HOLOOUTPUT[$LINE_POINTER] "ENDENDENDENDENDENDEND"
return
:DISPLAY_HOLO

setvar $HOLO_I 1
setvar $HOLO_PTR 1
setvar $HOLO_S ""
setvar $AVOIDFLAG ""
while (SECTOR.WARPS[$player~current_sector][$HOLO_I] > 0)
  setvar $HOLO_ADJ SECTOR.WARPS[$player~current_sector][$HOLO_I]
  if ((SECTOR.PLANETCOUNT[$HOLO_ADJ] > 0) or (SECTOR.TRADERCOUNT[$HOLO_ADJ] > 0) or (SECTOR.SHIPCOUNT[$HOLO_ADJ] > 0))
    setvar $FIGOWNER SECTOR.FIGS.OWNER[$HOLO_ADJ]
    if ((SECTOR.FIGS.QUANTITY[$HOLO_ADJ] >= 100) and (($FIGOWNER <> "belong to your Corp") or ($FIGOWNER <> "yours")))
      while ($HOLO_PTR <= $LINE_POINTER)
        getwordpos $HOLOOUTPUT[$HOLO_PTR] $HOLO_POS "Sector  : "&$HOLO_ADJ
        setvar $AVOIDFLAG $AVOIDFLAG&" "&$HOLO_ADJ
        if ($HOLO_POS <> 0)
          setvar $HOLO_S $HOLO_S&$HOLOOUTPUT[$HOLO_PTR]&"*"
          :LETS_GO_AGAIN
          add $HOLO_PTR 1
          getwordpos $HOLOOUTPUT[$HOLO_PTR] $POS "Warps to Sector(s) :"
          if (($HOLOOUTPUT[$HOLO_PTR] <> "") and ($POS = 0))
            setvar $HOLO_S $HOLO_S&$HOLOOUTPUT[$HOLO_PTR]&"*"
          else
            setvar $HOLO_S $HOLO_S&"         *"
            goto :DONE_SCAN
          end
          goto :LETS_GO_AGAIN
        end
        add $HOLO_PTR 1
      end
    end
  end
  :DONE_SCAN
  add $HOLO_I 1
end

setvar $HOLO_TARGETS "LSHRED_"&GAMENAME&".log"
if ($HOLO_S <> "")
  send "'*["&$TAGLINEB&"] SCAN RESULTS----------------------[ADJ SECTOR: "&CURRENTSECTOR&"*"
  send $HOLO_S&"* "
  waitfor "Sub-space comm-link terminated"
end
return
:RESETMINESAFTERRESTOCK


if (($DROP_ARMID > 0) and ($DROP_LIMP > 0))
  setvar $DROPING_MINES 3
elseif ($DROP_ARMID > 0)

  setvar $DROPING_MINES 2
elseif ($DROP_LIMP > 0)

  setvar $DROPING_MINES 1
else
  setvar $DROPING_MINES 0
end
return
:GETPERSONALPLANETS




setvar $planet~planetsinsectors SECTORS

send "cyq"
waitfor "<Computer activated>"
waitfor "Sector  Planet Name"
:PREAD

settextlinetrigger PREAD1 :PREAD1 "#"
settextlinetrigger PREADDONE :PREADDONE "======   ============  ==== ==== ==== ===== ===== "
settextlinetrigger PREADDONE2 :PREADDONE "No Planets claimed"
pause
:PREAD1
killalltriggers
getword CURRENTLINE $SECTOR 1
add $planet~planetsinsectors[$sector] 1
goto :PREAD
:PREADDONE

killalltriggers
return

# includes:
include "include/bot.ts"
include "include/switchboard.ts"
include "include/player.ts"
