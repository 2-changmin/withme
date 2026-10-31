using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Withme.Auth;

namespace Withme.Character
{
    public sealed class CharacterProfile
    {
        public int SchemaVersion=1;
        public string UserId,PetId,PetName,ModelId,RigJobId,ApprovedVersion;
        public bool Confirmed,QualityAccepted,PreferencesFinished;
        public AppearanceParameters Appearance=new AppearanceParameters();
        public int[] Preferences={-1,-1,-1,-1};
        public CharacterProfile Copy()=>JsonConvert.DeserializeObject<CharacterProfile>(JsonConvert.SerializeObject(this));
        public void Validate(string user,string pet)
        {
            if(SchemaVersion!=1||UserId!=user||PetId!=pet||!Guid.TryParse(user,out _)||!Guid.TryParse(pet,out _)||Appearance==null||Preferences==null||Preferences.Length!=4||Preferences.Any(a=>a< -1||a>1))throw new ApiFailure("CHECKPOINT_INVALID",0);
            RigSession.RouteId(ModelId);RigSession.RouteId(RigJobId);Appearance.Validate();
            if(Confirmed&&(!QualityAccepted||!Guid.TryParse(ApprovedVersion,out _)))throw new ApiFailure("CHECKPOINT_INVALID",0);
            if(PreferencesFinished&&(!Confirmed||Preferences.Any(a=>a<0)))throw new ApiFailure("CHECKPOINT_INVALID",0);
        }
    }
    public interface ICharacterProfiles
    {
        CharacterProfile Read(string user,string pet);
        CharacterProfile Confirm(CharacterProfile draft,bool qualityAccepted);
        CharacterProfile SavePreferences(CharacterProfile value);
    }
    // Explicit local fixture implementation. No real endpoint/production state
    // is invented while #24's persistence/approval contract is unavailable.
    public sealed class MockCharacterProfiles:ICharacterProfiles
    {
        readonly string directory;
        public MockCharacterProfiles(string directory){this.directory=directory;}
        string PathFor(string user,string pet)
        {if(!Guid.TryParse(user,out _)||!Guid.TryParse(pet,out _))throw new ApiFailure("CHECKPOINT_INVALID",0);return Path.Combine(directory,user+"-"+pet+".json");}
        public CharacterProfile Read(string user,string pet)
        {
            var path=PathFor(user,pet);if(!File.Exists(path))return null;
            try{var value=JsonConvert.DeserializeObject<CharacterProfile>(File.ReadAllText(path));if(value==null)throw new ApiFailure("CHECKPOINT_INVALID",0);value.Validate(user,pet);return value;}
            catch(Exception){throw new ApiFailure("CHECKPOINT_INVALID",0);}
        }
        public CharacterProfile Confirm(CharacterProfile draft,bool qualityAccepted)
        {
            if(!qualityAccepted)throw new ApiFailure("QUALITY_REVIEW_REQUIRED",0);
            var next=draft.Copy();var previous=Read(draft.UserId,draft.PetId);
            if(previous!=null&&previous.Confirmed&&previous.ModelId==next.ModelId&&previous.RigJobId==next.RigJobId&&JsonConvert.SerializeObject(previous.Appearance)==JsonConvert.SerializeObject(next.Appearance))return previous;
            next.Confirmed=true;next.QualityAccepted=true;next.ApprovedVersion=Guid.NewGuid().ToString();
            if(previous!=null){next.Preferences=(int[])previous.Preferences.Clone();next.PreferencesFinished=previous.PreferencesFinished;}
            return Save(next);
        }
        public CharacterProfile SavePreferences(CharacterProfile value)
        {
            var previous=Read(value.UserId,value.PetId);
            if(previous==null||!previous.Confirmed||previous.ApprovedVersion!=value.ApprovedVersion)throw new ApiFailure("CONFLICT",409);
            // Preference updates must not modify the approved appearance/model.
            previous.Preferences=(int[])value.Preferences.Clone();previous.PreferencesFinished=value.PreferencesFinished;return Save(previous);
        }
        CharacterProfile Save(CharacterProfile value)
        {
            value.Validate(value.UserId,value.PetId);var path=PathFor(value.UserId,value.PetId);
            try{Directory.CreateDirectory(directory);var temporary=path+".tmp";File.WriteAllText(temporary,JsonConvert.SerializeObject(value));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);return value.Copy();}
            catch(Exception){throw new ApiFailure("STORAGE",0);}
        }
    }
    public sealed class UnavailableCharacterProfiles:ICharacterProfiles
    {
        public CharacterProfile Read(string user,string pet)=>throw new ApiFailure("NOT_IMPLEMENTED",501);
        public CharacterProfile Confirm(CharacterProfile draft,bool accepted)=>throw new ApiFailure("NOT_IMPLEMENTED",501);
        public CharacterProfile SavePreferences(CharacterProfile value)=>throw new ApiFailure("NOT_IMPLEMENTED",501);
    }
}
