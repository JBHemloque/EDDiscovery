﻿/*
 * Copyright © 2016 - 2018 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 * 
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */

using System;
using System.Diagnostics;
using System.Linq;
using EliteDangerousCore.DB;
using EliteDangerousCore.JournalEvents;
using EliteDangerousCore.EDSM;
using System.Data.Common;

namespace EliteDangerousCore
{
    [DebuggerDisplay("Event {EntryType} {System.Name} ({System.X,nq},{System.Y,nq},{System.Z,nq}) {EventTimeUTC} Inx:{Indexno} JID:{Journalid}")]
    public class HistoryEntry           // DONT store commander ID.. this history is externally filtered on it.
    {
        #region Variables

        public int Indexno;            // for display purposes.  from 1 to number of records

        public JournalEntry journalEntry;       // MUST be present

        public ISystem System;         // Must be set! All entries, even if they are not FSD entries.
                                       // The Minimum is name and edsm_id 
                                       // x/y/z can be NANs or position. 
                                       // if edsm_id = 0, no edsm match was found.
                                       // when the front end needs to use it, it will call EnsureSystemEDSM/FillInPositions to see if it can be filled up
                                       // if System.status != SystemStatusEnum.EDSC then its presumed its an inmemory load and the system table can be checked
                                       //       and if edsm_id>0 a load from SystemTable will occur with the edsm_id used
                                       //       if edsm_id=-1 a load from SystemTable will occur with the name used
                                       // SO the journal reader can just read data in that table only, does not need to do a system match

        public JournalTypeEnum EntryType { get { return journalEntry.EventTypeID; } }
        public long Journalid { get { return journalEntry.Id; } }
        public EDCommander Commander { get { return EDCommander.GetCommander(journalEntry.CommanderId); } }
        public DateTime EventTimeLocal { get { return EventTimeUTC.ToLocalTime(); } }
        public DateTime EventTimeUTC { get { return journalEntry.EventTimeUTC; } }
        public TimeSpan AgeOfEntry() { return DateTime.Now - EventTimeUTC; }

        public string EventSummary { get { return journalEntry.EventSummaryName;} }

        public bool EdsmSync { get { return journalEntry.SyncedEDSM; } }           // flag populated from journal entry when HE is made. Have we synced?
        public bool EDDNSync { get { return journalEntry.SyncedEDDN; } }
        public bool EGOSync { get { return journalEntry.SyncedEGO; } }
        public bool StartMarker { get { return journalEntry.StartMarker; } }
        public bool StopMarker { get { return journalEntry.StopMarker; } }
        public bool IsFSDJump { get { return EntryType == JournalTypeEnum.FSDJump; } }
        public bool IsLocOrJump { get { return EntryType == JournalTypeEnum.FSDJump || EntryType == JournalTypeEnum.Location; } }
        public bool IsFuelScoop { get { return EntryType == JournalTypeEnum.FuelScoop; } }
        public bool IsShipChange { get { return (EntryType == JournalTypeEnum.LoadGame || EntryType == JournalTypeEnum.Docked) && ShipInformation != null; } }
        public bool IsBetaMessage { get { return journalEntry?.Beta ?? false; } }

        public double TravelledDistance { get { return travelled_distance; } }
        public TimeSpan TravelledSeconds { get { return travelled_seconds; } }
        public bool isTravelling { get { return travelling; } }
        public int TravelledMissingjump { get { return travelled_missingjump; } }
        public int Travelledjumps { get { return travelled_jumps; } }
        public string TravelledJumpsAndMisses { get { return travelled_jumps.ToStringInvariant() + ((travelled_missingjump > 0) ? (" (" + travelled_missingjump.ToStringInvariant() + ")") : ""); } }

        public bool IsLanded { get { return EntryStatus.TravelState == HistoryEntryStatus.TravelStateType.Landed; } }
        public bool IsDocked { get { return EntryStatus.TravelState == HistoryEntryStatus.TravelStateType.Docked; } }
        public bool IsInHyperSpace { get { return EntryStatus.TravelState == HistoryEntryStatus.TravelStateType.Hyperspace; } }
        public string WhereAmI { get { return EntryStatus.StationName ?? EntryStatus.BodyName ?? "Unknown"; } }
        public string BodyType { get { return EntryStatus.BodyType ?? "Unknown"; } }
        public string ShipType { get { return EntryStatus.ShipType ?? "Unknown"; } }         // NOT FD - translated name
        public int ShipId { get { return EntryStatus.ShipID; } }
        public bool MultiPlayer { get { return EntryStatus.OnCrewWithCaptain != null; } }
        public string GameMode { get { return EntryStatus.GameMode ?? ""; } }
        public string Group { get { return EntryStatus.Group ?? ""; } }
        public string GameModeGroup { get { return GameMode + (String.IsNullOrEmpty(Group) ? "" : (":" + Group)); } }
        public bool Wanted { get { return EntryStatus.Wanted; } }
        public long? MarketID { get { return EntryStatus.MarketId; } }

        public string DebugStatus { get {      // Use as a replacement for note in travel grid to debug
                return
                     WhereAmI
                     + ", " +  (EntryStatus.BodyType ?? "Null")
                     + "," + (EntryStatus.BodyName ?? "Null")
                     + " SN:" + (EntryStatus.StationName ?? "Null") 
                     + " ST:" + (EntryStatus.StationType ?? "Null")
                     + " T:" + EntryStatus.TravelState
                     + " S:" + EntryStatus.ShipID + "," + EntryStatus.ShipType
                     + " GM:" + EntryStatus.GameMode
                     + " W:" + EntryStatus.Wanted
                     + " BA:" + EntryStatus.BodyApproached 
                     ;
            } }
  
        public long Credits { get; set; }       // set up by Historylist during ledger accumulation

        public bool ContainsRares() // function due to debugger and cost of working out
        {
            return materialscommodities != null && materialscommodities.ContainsRares();
        }

        // Calculated values, not from JE

        public MaterialCommoditiesList MaterialCommodity { get { return materialscommodities; } }
        public ShipInformation ShipInformation { get { return shipmodules; } set { shipmodules = value; } }     // may be null if not set up yet
        public ModulesInStore StoredModules { get { return storedmodules; } set { storedmodules = value; } }
        public MissionList MissionList { get { return missionlist; } set { missionlist = value; } }

        public SystemNoteClass snc;     // system note class found attached to this entry. May be null

        private HistoryEntryStatus EntryStatus { get;  set; }
        private double travelled_distance;  // start/stop distance and time computation
        private TimeSpan travelled_seconds;
        private bool travelling;

        private int travelled_missingjump;
        private int travelled_jumps;

        private MaterialCommoditiesList materialscommodities;
        private ShipInformation shipmodules;
        private ModulesInStore storedmodules;
        private MissionList missionlist;                    // mission state at this point..

        #endregion

        #region Constructors

        private HistoryEntry()
        {
        }

        public static HistoryEntry FromJournalEntry(JournalEntry je, HistoryEntry prev, out bool journalupdate, SQLiteConnectionSystem conn = null)
        {
            ISystem isys = prev == null ? new SystemClass("Unknown") : prev.System;
            int indexno = prev == null ? 1 : prev.Indexno + 1;

            journalupdate = false;

            if (je.EventTypeID == JournalTypeEnum.Location || je.EventTypeID == JournalTypeEnum.FSDJump)
            {
                JournalLocOrJump jl = je as JournalLocOrJump;

                ISystem newsys;

                if (jl != null && jl.HasCoordinate)       // LAZY LOAD IF it has a co-ord.. the front end will when it needs it
                {
                    newsys = new SystemClass(jl.StarSystem, jl.StarPos.X, jl.StarPos.Y, jl.StarPos.Z)
                    {
                        EDSMID = jl.EdsmID < 0 ? 0 : jl.EdsmID,       // pass across the EDSMID for the lazy load process.
                        Faction = jl.Faction,
                        Government = jl.EDGovernment,
                        PrimaryEconomy = jl.EDEconomy,
                        Security = jl.EDSecurity,
                        Population = jl.Population ?? 0,
                        State = jl.EDState,
                        Allegiance = jl.EDAllegiance,
                        UpdateDate = jl.EventTimeUTC,
                        status = SystemStatusEnum.EDDiscovery,
                        SystemAddress = jl.SystemAddress,
                    };

                    // If it was a new system, pass the coords back to the StartJump
                    if (prev != null && prev.journalEntry is JournalStartJump)
                    {
                        prev.System = newsys;       // give the previous startjump our system..
                    }
                }
                else
                {
                    // NOTE Rob: 09-JAN-2018 I've removed the Jumpstart looking up a system by name since they were using up lots of lookup time during history reading.  
                    // This is used for pre 2.2 systems without co-ords, which now should be limited.
                    // JumpStart still gets the system when the FSD loc is processed, see above.
                    // Jumpstart was also screwing about with the EDSM ID fill in which was broken.  This is now working again.

                    // Default one
                    newsys = new SystemClass(jl.StarSystem);
                    newsys.EDSMID = je.EdsmID;

                    ISystem s = SystemCache.FindSystem(newsys, conn);      // has no co-ord, did we find it?

                    if (s != null)                               // found a system..
                    {
                        if (jl != null && jl.HasCoordinate)         // if journal Loc, and journal has a star position, use that instead of EDSM..
                        {
                            s.X = Math.Round(jl.StarPos.X * 32.0) / 32.0;
                            s.Y = Math.Round(jl.StarPos.Y * 32.0) / 32.0;
                            s.Z = Math.Round(jl.StarPos.Z * 32.0) / 32.0;
                        }

                        //Debug.WriteLine("HistoryList found system {0} {1}", s.id_edsm, s.name);
                        newsys = s;

                        if (jl != null && je.EdsmID <= 0 && newsys.EDSMID > 0) // only update on a JL..
                        {
                            journalupdate = true;
                            Debug.WriteLine("HE EDSM ID update requested {0} {1}", newsys.EDSMID, newsys.Name);
                        }
                    }
                    else
                        newsys.EDSMID = -1;        // mark as checked but not found
                }

                JournalFSDJump jfsd = je as JournalFSDJump;

                if (jfsd != null)
                {
                    if (jfsd.JumpDist <= 0 && isys.HasCoordinate && newsys.HasCoordinate) // if no JDist, its a really old entry, and if previous has a co-ord
                    {
                        jfsd.JumpDist = isys.Distance(newsys); // fill it out here

                        if (jfsd.JumpDist > 0)
                        {
                            journalupdate = true;
                            Debug.WriteLine("Je Jump distance update(3) requested {0} {1} {2}", newsys.EDSMID, newsys.Name, jfsd.JumpDist);
                        }
                    }

                }

                isys = newsys;
            }

            HistoryEntry he = new HistoryEntry
            {
                Indexno = indexno,
                journalEntry = je,
                System = isys,
                EntryStatus = HistoryEntryStatus.Update(prev?.EntryStatus, je, isys.Name)
            };

            if (prev != null )
            {
                if (prev.StopMarker)            // if we had a stop marker previously, means the next one needs to clear the counters
                {
                    he.travelling = false;               // still travelling if its a start marker
                    he.travelled_distance = 0;
                    he.travelled_seconds = new TimeSpan(0);
                    he.travelled_missingjump = 0;
                    he.travelled_jumps = 0;
                }
                else
                {
                    he.travelling = prev.travelling;
                    he.travelled_distance = prev.travelled_distance;
                    he.travelled_seconds = prev.travelled_seconds;
                    he.travelled_missingjump = prev.travelled_missingjump;
                    he.travelled_jumps = prev.travelled_jumps;
                }
            }

            if (he.StartMarker)           // start marker, start travelling
                he.travelling = true;

            if ( he.travelling )
            {
                if (he.IsFSDJump && !he.MultiPlayer)   // if jump, and not multiplayer..
                {
                    double dist = ((JournalFSDJump)je).JumpDist;
                    if (dist <= 0)
                        he.travelled_missingjump++;
                    else
                    {
                        he.travelled_distance += dist;
                        he.travelled_jumps++;
                    }
                }

                if (prev != null)
                {
                    TimeSpan diff = he.EventTimeUTC.Subtract(prev.EventTimeUTC);

                    if (he.EntryType != JournalTypeEnum.LoadGame && diff < new TimeSpan(2, 0, 0))   // time between last entry and load game is not real time
                    {
                        he.travelled_seconds += diff;
                    }
                }
            }

            return he;
        }

        public void ProcessWithUserDb(JournalEntry je, HistoryEntry prev, HistoryList hl, SQLiteConnectionUser conn)      // called after above with a USER connection
        {
            materialscommodities = MaterialCommoditiesList.Process(je, prev?.materialscommodities, conn, EliteConfigInstance.InstanceConfig.ClearMaterials, EliteConfigInstance.InstanceConfig.ClearCommodities);

            snc = SystemNoteClass.GetSystemNote(Journalid, IsFSDJump, System);       // may be null
        }

        public void SetJournalSystemNoteText(string text, bool commit, bool sendtoedsm)
        {
            if (snc == null || snc.Journalid == 0)           // if no system note, or its one on a system, from now on we assign journal system notes only from this IF
                snc = SystemNoteClass.MakeSystemNote("", DateTime.Now, System.Name, Journalid, System.EDSMID, IsFSDJump);

            snc = snc.UpdateNote(text, commit, DateTime.Now, snc.EdsmId, IsFSDJump);        // and update info, and update our ref in case it has changed or gone null
                                                                                            // remember for EDSM send purposes if its an FSD entry

            if (snc != null && commit && sendtoedsm && snc.FSDEntry)                    // if still have a note, and commiting, and send to esdm, and FSD jump
                EDSMClass.SendComments(snc.SystemName, snc.Note, snc.EdsmId);
        }

        #endregion

        public string TravelInfo()          // use to get a summary of travel info.. 
        {
            if (StopMarker)
            {
                return "Travelled " + travelled_distance.ToStringInvariant("0.0") + " LY"
                                     + ", " + travelled_jumps + " jumps"
                                     + ((travelled_missingjump > 0) ? ", " + travelled_missingjump + " unknown distance jumps" : "") +
                                      ", time " + travelled_seconds;
            }
            else if (isTravelling && IsFSDJump)
            {
                return "Travelling distance " + travelled_distance.ToString("0.0") + " LY"
                + ", " + travelled_jumps + " jumps"
                + ((travelled_missingjump > 0) ? ", " + travelled_missingjump + " unknown distance jumps" : "") +
                ", time " + travelled_seconds;
            }
            else
                return null;
        }

        public System.Drawing.Image GetIcon
        {
            get
            {
                if (journalEntry != null)
                    return journalEntry.Icon;
                else if (EntryType == JournalTypeEnum.FSDJump)
                    return JournalEntry.JournalTypeIcons[JournalTypeEnum.FSDJump];
                else
                    return JournalEntry.JournalTypeIcons[JournalTypeEnum.Unknown];
            }
        }


        public void UpdateMapColour(int v)
        {
            if (EntryType == JournalTypeEnum.FSDJump)
                (journalEntry as JournalFSDJump).UpdateMapColour(v);
        }

        public void UpdateCommanderID(int v)
        {
            journalEntry.UpdateCommanderID(v);
        }

        public void SetEdsmSync(SQLiteConnectionUser cn = null, DbTransaction txn = null)
        {
            journalEntry.UpdateSyncFlagBit(SyncFlags.EDSM, true, SyncFlags.NoBit, false, cn, txn);
        }

        public void SetEddnSync(SQLiteConnectionUser cn = null, DbTransaction txn = null)
        {
            journalEntry.UpdateSyncFlagBit(SyncFlags.EDDN, true, SyncFlags.NoBit, false, cn, txn);
        }

        public void SetEGOSync(SQLiteConnectionUser cn = null, DbTransaction txn = null)
        {
            journalEntry.UpdateSyncFlagBit(SyncFlags.EGO, true, SyncFlags.NoBit, false, cn, txn);
        }

        public bool IsJournalEventInEventFilter(string[] events)
        {
            return events.Contains(EntryType.ToString().SplitCapsWord());
        }

        public bool IsJournalEventInEventFilter(string eventstr)
        {
            return eventstr == "All" || IsJournalEventInEventFilter(eventstr.Split(';'));
        }

        public EliteDangerousCalculations.FSDSpec.JumpInfo GetJumpInfo()      // can we calc jump range? null if we don't have the info
        {
            EliteDangerousCalculations.FSDSpec fsdspec = ShipInformation?.GetFSDSpec();

            if (fsdspec != null)
            {
                double mass = ShipInformation.HullMass() + ShipInformation.ModuleMass();

                if (mass > 0)
                    return fsdspec.GetJumpInfo(MaterialCommodity.CargoCount, mass, ShipInformation.FuelLevel, ShipInformation.FuelCapacity/2);
            }

            return null;
        }

    }
}
