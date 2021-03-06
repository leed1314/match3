﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Facebook.Unity;
using UnityEngine.SceneManagement;

public class FacebookManager : MonoBehaviour
{
    private static FacebookManager _instance;

    private readonly string _ppUserLivesRequestKey = "RequestLives";
    private readonly string _ppUserSendLivesKey = "SendLives";

    private FacebookUserInfo _currentUserFacebookUserInfo = new FacebookUserInfo();
    public FacebookUserInfo CurrentUserFacebookUserInfo
    {
        get { return _currentUserFacebookUserInfo; }
        private set { _currentUserFacebookUserInfo = value; }
    }

    private List<FacebookUserInfo> _friendUserFacebookInfos = new List<FacebookUserInfo>();
    public List<FacebookUserInfo> FriendUserFacebookInfos
    {
        get { return _friendUserFacebookInfos; }
        private set { _friendUserFacebookInfos = value; }
    }

    private List<FacebookUserInfo> _inventableFriendsList = new List<FacebookUserInfo>();
    public List<FacebookUserInfo> InventableFriendsList
    {
        get { return _inventableFriendsList; }
        private set { _inventableFriendsList = value; }
    }

    private List<UserRequestInfo> _userRequests = new List<UserRequestInfo>();
    public List<UserRequestInfo> UserRequests
    {
        get { return _userRequests; }
    }

    private Dictionary<string, bool> _userFacebookPermissions = new Dictionary<string, bool>();

    public bool PublishPermissionRequested
    {
        get
        {
            return _userFacebookPermissions.ContainsKey("publish_actions");
        }
    }

    public static FacebookManager Instance
    {
        get { return _instance; }
    }

    public bool IsInitialized
    {
        get { return FB.IsInitialized; }
    }

    public bool IsLoggedIn
    {
        get { return FB.IsLoggedIn; }
    }

    public Dictionary<string, bool> UserFacebookPermissions
    {
        get { return _userFacebookPermissions; }
    }

    public Action OnFbLoggedIn { get; set; }
    public Action OnFbLoginCancelled { get; set; }
    public Action<string> OnFbLoginError { get; set; }
    public Action OnFbLoggedOut { get; set; }

    public Action OnUserInfoDownloadedEvent { get; set; }
    public Action OnFriendsInfoDownloadedEvent { get; set; }
    public Action OnInvintableFriendsInfoDownloadedEvent { get; set; }
    public Action OnGetRequestsEvent { get; set; }

    public Action OnSendInviteSuccess { get; set; }
    public Action OnSendLivesSuccess { get; set; }
    public Action OnSendLivesRequestSuccess { get; set; }

    #region UnityMethods
    private void Awake()
    {
        DontDestroyOnLoad(transform.gameObject);
        if (_instance == null)
        {
            _instance = this;
        }
    }

    private void Start()
    {
        if (!FB.IsInitialized)
        {
            FB.Init(OnFbInitComplete);
        }

        SceneManager.activeSceneChanged += (sc1, sc2) =>
        {
            Debug.LogFormat("Scene changed: {0} to {1}", sc1.name, sc2.name);
        };
    }
    #endregion

    #region Callbacks
    private void OnFbInitComplete()
    {
        Debug.Log("Facebook init complete");
        FB.ActivateApp();

        if (FB.IsLoggedIn)
        {
            GetFbUserInfo();
            if (OnFbLoggedIn != null)
            {
                OnFbLoggedIn.Invoke();
            }
        }
    }

    private void FbLoginCallback(ILoginResult result)
    {
        if (result.Cancelled)
        {
            if (OnFbLoginCancelled != null)
            {
                OnFbLoginCancelled.Invoke();
            }
            return;
        }

        if (!string.IsNullOrEmpty(result.Error))
        {
            Debug.LogError("Facebook login error: " + result.Error);
            if (OnFbLoginError != null)
            {
                OnFbLoginError.Invoke(result.Error);
            }
            return;
        }

        if (OnFbLoggedIn != null)
        {
            OnFbLoggedIn.Invoke();
        }
        //Debug.Log("Facebook logged in");
        GetFbUserInfo();
    }

    private void OnFriendsDownloaded(IGraphResult result)
    {
        if (result.Error != null)
        {
            Debug.LogError("Error getting FB friends: " + result.Error);
        }
        else
        {
            //Debug.Log(result.RawResult);
            Dictionary<string, object> responseObject = Facebook.MiniJSON.Json.Deserialize(result.RawResult) as Dictionary<string, object>;
            StartCoroutine(GetFriendsFacebookUserInfo(responseObject["data"] as List<object>, UserType.Friend));
        }
    }

    private void OnInvitableFriendsDownloaded(IGraphResult result)
    {
        if (result.Error != null)
        {
            Debug.LogError("Error getting FB invitable friends: " + result.Error);
        }
        else
        {
            //Debug.Log(result.RawResult);
            Dictionary<string, object> responseObject = Facebook.MiniJSON.Json.Deserialize(result.RawResult) as Dictionary<string, object>;
            StartCoroutine(GetFriendsFacebookUserInfo(responseObject["data"] as List<object>, UserType.Invitable));
        }
    }

    private void GetFriendRequestsCallback(IGraphResult result)
    {
        if (!string.IsNullOrEmpty(result.Error))
        {
            Debug.LogError("Can`t get app requests: " + result.Error);
        }

        //Debug.Log(result.RawResult);
        Dictionary<string, object> responseObject = Facebook.MiniJSON.Json.Deserialize(result.RawResult) as Dictionary<string, object>;

        var dataObject = responseObject["data"] as List<object>;

        foreach (var data in dataObject)
        {
            var requestData = new UserRequestInfo();
            Dictionary<string, object> friendDataObjectDict = data as Dictionary<string, object>;

            requestData.Id = friendDataObjectDict["id"].ToString();
            requestData.Message = friendDataObjectDict["message"].ToString();
            requestData.CreatedTime = friendDataObjectDict["created_time"].ToString();

            var fromData = friendDataObjectDict["from"] as Dictionary<string, object>;
            requestData.User = _friendUserFacebookInfos.Find(f => f.id == fromData["id"].ToString());

            if (friendDataObjectDict["data"].ToString() == "item_life")
            {
                requestData.Type = RequestType.SendLife;
            }
            else if (friendDataObjectDict["data"].ToString() == "request_item_life")
            {
                requestData.Type = RequestType.RequestLife;    
            }

            _userRequests.Add(requestData);
        }

        if (_userRequests != null && _userRequests.Any())
        {
            if (OnGetRequestsEvent != null)
            {
                OnGetRequestsEvent.Invoke();
            }
        }
    }

    private void SendLifeRequestCallback(IAppRequestResult result)
    {
        if (!string.IsNullOrEmpty(result.Error))
        {
            Debug.LogError("Lives request not sended with error: " + result.Error);
        }

        RequestsSendData data;
        if (PlayerPrefs.HasKey(_ppUserLivesRequestKey))
        {
            data = JsonUtility.FromJson<RequestsSendData>(PlayerPrefs.GetString(_ppUserLivesRequestKey));
            foreach (var id in result.To)
            {
                if (!data.IdsList.Contains(id))
                {
                    data.IdsList.Add(id);
                }
            }
        }
        else
        {
            data = new RequestsSendData(DateTime.Now, result.To.ToList());
        }

        var dataString = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(_ppUserSendLivesKey, dataString);

        if (OnSendLivesRequestSuccess != null)
        {
            OnSendLivesRequestSuccess.Invoke();
        }

        UpdateFriendsList();
    }

    private void SendLiveCallback(IAppRequestResult result)
    {
        if (!string.IsNullOrEmpty(result.Error))
        {
            Debug.LogError("Lives not sended with error: " + result.Error);
        }

        RequestsSendData data;
        if (PlayerPrefs.HasKey(_ppUserSendLivesKey))
        {
            data = JsonUtility.FromJson<RequestsSendData>(PlayerPrefs.GetString(_ppUserSendLivesKey));
            foreach (var id in result.To)
            {
                if (!data.IdsList.Contains(id))
                {
                    data.IdsList.Add(id);
                }
            }
        }
        else
        {
            data = new RequestsSendData(DateTime.Now, result.To.ToList());
        }
        
        var dataString = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(_ppUserSendLivesKey, dataString);

        if (OnSendLivesSuccess != null)
        {
            OnSendLivesSuccess.Invoke();
        }

        UpdateFriendsList();
    }

    private void InvitesCallback(IAppRequestResult result)
    {
        if (!string.IsNullOrEmpty(result.Error))
        {
            Debug.LogError("Request not sended wit error: " + result.Error);
        }

        if (OnSendInviteSuccess != null)
        {
            OnSendInviteSuccess.Invoke();
        }

        UpdateInventableFriendsList();
    }

    #endregion

    #region Private Methods

    private void GetFbUserInfo()
    {
        _userFacebookPermissions.Clear();
        foreach (var permission in AccessToken.CurrentAccessToken.Permissions)
        {
            _userFacebookPermissions.Add(permission, true);
        }
        StartCoroutine(GetFacebookUserInfo(graphURL: "/me?fields=id,name,picture.width(64).height(64)", userType: UserType.Current));
    }

    private IEnumerator GetFacebookUserInfo(string graphURL = null, object data = null, UserType userType = UserType.Current)
    {
        bool finishedGettingInfo = false;

        FacebookUserInfo userInfo = new FacebookUserInfo();

        if (!string.IsNullOrEmpty(graphURL))
        {
            FB.API(graphURL, HttpMethod.GET, userInfoResult => {
                if (userInfoResult.Error != null)
                {
                    Debug.LogError("Error getting FB user info: " + userInfoResult.Error);
                }
                else
                {
                    Dictionary<string, object> userInfoObjects = Facebook.MiniJSON.Json.Deserialize(userInfoResult.RawResult) as Dictionary<string, object>;
                    userInfo.id = userInfoObjects["id"].ToString();
                    var userFullName = userInfoObjects["name"].ToString();
                    var s = userFullName.Split(new char[] { ' ' });
                    userInfo.firstName = s[0];
                    userInfo.lastName = s[1];
                    var pictureData = userInfoObjects["picture"] as Dictionary<string, object>;
                    var p = pictureData["data"] as Dictionary<string, object>;
                    userInfo.pictureUrl = p["url"].ToString();
                }
                finishedGettingInfo = true;
            });
        }

        if (data != null)
        {
            var dictionaryDataObject = data as Dictionary<string, object>;
            userInfo.id = dictionaryDataObject["id"].ToString();
            var userFullName = dictionaryDataObject["name"].ToString();
            var s = userFullName.Split(new char[] { ' ' });
            userInfo.firstName = s[0];
            userInfo.lastName = s[1];

            var pictureData = dictionaryDataObject["picture"] as Dictionary<string, object>;
            var p = pictureData["data"] as Dictionary<string, object>;
            userInfo.pictureUrl = p["url"].ToString();

            finishedGettingInfo = true;
        }

        while (!finishedGettingInfo)
        {
            yield return null;
        }

        if (userInfo.pictureUrl != null)
        {
            yield return StartCoroutine(CheckAndLoadImage(userInfo.id, userInfo.pictureUrl, !userType.Equals(UserType.Invitable), (texture) =>
            {
                userInfo.ProfilePicture = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
            }));
        }

        if (userType.Equals(UserType.Current))
        {
            CurrentUserFacebookUserInfo = userInfo;
            if (OnUserInfoDownloadedEvent != null)
            {
                OnUserInfoDownloadedEvent.Invoke();
            }

            UpdateFriendsList();
            UpdateInventableFriendsList();
        }
        else if (userType.Equals(UserType.Friend))
        {
            FriendUserFacebookInfos.Add(userInfo);
        }
        else
        {
            InventableFriendsList.Add(userInfo);
        }
    }

    private IEnumerator CheckAndLoadImage(string userId, string url, bool save, Action<Texture2D> onImageLoaded)
    {
        FileInfo photoFileInfo = new FileInfo(Application.persistentDataPath + "/images/" + userId + ".png");
        if (photoFileInfo.Exists)
        {
            yield return StartCoroutine(LoadImageFromLocal(userId, onImageLoaded));
        }
        else
        {
            yield return StartCoroutine(LoadImageFromInternet(userId, url, save, onImageLoaded));
        }
        yield return null;
    }

    private IEnumerator LoadImageFromLocal(string userId, Action<Texture2D> onImageLoaded)
    {
        var url = "file:///"+Application.persistentDataPath + "/images/" + userId + ".png";
        var www = new WWW(url);
        yield return www;

        if (www.isDone)
        {
            if (onImageLoaded != null)
            {
                onImageLoaded.Invoke(www.texture);
            }
        }
    }

    private IEnumerator LoadImageFromInternet(string userId, string url, bool save, Action<Texture2D> onImageLoaded)
    {
        var www = new WWW(url);
        yield return www;

        if (www.isDone)
        {
            if (save)
            {
                SaveTextureToFile(www.texture, userId);
            }

            if (onImageLoaded != null)
            {
                onImageLoaded.Invoke(www.texture);
            }
        }
    }

    private void SaveTextureToFile(Texture2D texture, string filename)
    {
        System.IO.DirectoryInfo directoryInfo = new DirectoryInfo(Application.persistentDataPath + "/images/");

        if (directoryInfo.Exists)
        {
            var bytes = texture.EncodeToPNG();
            var fileSave = new FileStream(Application.persistentDataPath + "/images/" + filename + ".png", FileMode.Create);
            var binary = new BinaryWriter(fileSave);
            binary.Write(bytes);
            fileSave.Close();
        }
        else
        {
            directoryInfo.Create();
            var bytes = texture.EncodeToPNG();
            var fileSave = new FileStream(Application.persistentDataPath + "/images/" + filename + ".png", FileMode.Create);
            var binary = new BinaryWriter(fileSave);
            binary.Write(bytes);
            fileSave.Close();
        }
        
    }

    private void UpdateInventableFriendsList()
    {
        if (_inventableFriendsList != null && _inventableFriendsList.Any())
        {
            _inventableFriendsList.Clear();
        }
        FB.API("/me/invitable_friends?limit=100&fields=id,name,picture.width(64).height(64)", HttpMethod.GET, OnInvitableFriendsDownloaded);
    }

    private void UpdateFriendsList()
    {
        if (_friendUserFacebookInfos != null && _friendUserFacebookInfos.Any())
        {
            _friendUserFacebookInfos.Clear();
        }
        FB.API("/me/friends?limit=100", HttpMethod.GET, OnFriendsDownloaded);
    }

    private IEnumerator GetFriendsFacebookUserInfo(List<object> responseDataObjects, UserType userType)
    {
        int numFriends = responseDataObjects.Count;
        foreach (object friendDataObject in responseDataObjects)
        {
            Dictionary<string, object> friendDataObjectDict = friendDataObject as Dictionary<string, object>;
            if (userType.Equals(UserType.Friend))
            {
                var request = string.Format("/{0}{1}", friendDataObjectDict["id"], "?fields=id,name,picture.width(64).height(64)");
                yield return StartCoroutine(GetFacebookUserInfo(graphURL: request, userType: userType));
            }
            if (userType.Equals(UserType.Invitable))
            {
                yield return StartCoroutine(GetFacebookUserInfo(data: friendDataObject, userType: userType));
            }

            if (FriendUserFacebookInfos.Count == numFriends)
            {
                if (OnFriendsInfoDownloadedEvent != null)
                {
                    OnFriendsInfoDownloadedEvent.Invoke();
                }
                
                GetFriendRequests();
            }

            if (InventableFriendsList.Count == numFriends)
            {
                if (OnInvintableFriendsInfoDownloadedEvent != null)
                {
                    OnInvintableFriendsInfoDownloadedEvent.Invoke();
                }
            }
        }
    }

    private void GetFriendRequests()
    {
        if (FB.IsInitialized)
        {
            _userRequests.Clear();
            FB.API("/me/apprequests", HttpMethod.GET, GetFriendRequestsCallback);
        }
    }
#endregion

#region Public Methods
    public FacebookManager UpdateFriends()
    {
        UpdateInventableFriendsList();
        UpdateFriendsList();
        return this;
    }

    public FacebookManager LogInFacebook()
    {
        if (!FB.IsLoggedIn)
        {
            string[] permissions = new string[3];
            permissions[0] = "public_profile";
            permissions[1] = "user_friends";
            permissions[2] = "email";
            FB.LogInWithReadPermissions(permissions: permissions, callback: FbLoginCallback);
        }
        return this;
    }

    public FacebookManager LogOutFacebook()
    {
        if (FB.IsLoggedIn)
        {
            FB.LogOut();

            if (OnFbLoggedOut != null)
            {
                OnFbLoggedOut.Invoke();
            }
        }
        return this;
    }

    public AccessToken GetAccessToken()
    {
        return AccessToken.CurrentAccessToken;
    }

    public List<FacebookUserInfo> GetLifeRequestAvailableFriends()
    {
        var list = new List<FacebookUserInfo>();
        if (PlayerPrefs.HasKey(_ppUserLivesRequestKey))
        {
            DateTime parsedDateTime;
            var data = JsonUtility.FromJson<RequestsSendData>(PlayerPrefs.GetString(_ppUserLivesRequestKey));
            if (DateTime.TryParse(data.LastSendDateTime, out parsedDateTime))
            {
                if (DateTime.Now > parsedDateTime.AddHours(24))
                {
                    return _friendUserFacebookInfos;
                }
            }
            foreach (var user in _friendUserFacebookInfos)
            {
                if (!data.IdsList.Contains(user.id))
                {
                    list.Add(user);
                }
            }
            return list;
        }
        else
        {
            return _friendUserFacebookInfos;
        }
    }

    public List<FacebookUserInfo> GetLifeSendAvailableFriends()
    {
        var list = new List<FacebookUserInfo>();
        if (PlayerPrefs.HasKey(_ppUserSendLivesKey))
        {
            DateTime parsedDateTime;
            var data = JsonUtility.FromJson<RequestsSendData>(PlayerPrefs.GetString(_ppUserSendLivesKey));
            if (DateTime.TryParse(data.LastSendDateTime, out parsedDateTime))
            {
                if (DateTime.Now > parsedDateTime.AddHours(24))
                {
                    return _friendUserFacebookInfos;
                }
            }
            foreach (var user in _friendUserFacebookInfos)
            {
                if (!data.IdsList.Contains(user.id))
                {
                    list.Add(user);
                }
            }
            return list;
        }
        else
        {
            return _friendUserFacebookInfos;
        }
    }

    public FacebookManager ConfirmRequests(List<UserRequestInfo> requests)
    {
        foreach (var request in requests)
        {
            FB.API("/" + request.Id, HttpMethod.DELETE);
        }
        return this;
    }

    public FacebookManager SendInvites(List<string> userInviteIds)
    {
        FB.AppRequest("Try it!", to: userInviteIds, callback: InvitesCallback);
        return this;
    }

    public FacebookManager SendLives(List<string> userIds)
    {
        FB.AppRequest(message: "I sent you a life!", to: userIds, maxRecipients: null, title: "Send a life for your friends.", data: "item_life", callback: SendLiveCallback);
        return this;
    }

    public FacebookManager SendLivesRequest(List<string> userIds)
    {
        FB.AppRequest(message: "Help me with lives!", to: userIds, maxRecipients: null, title: "Send lives request.", data: "request_item_life", callback: SendLifeRequestCallback);
        return this;
    }
#endregion
}

[System.Serializable]
public class RequestsSendData
{
    public string LastSendDateTime;
    public List<string> IdsList;

    public RequestsSendData(DateTime time, List<string> idsList)
    {
        LastSendDateTime = time.ToString(CultureInfo.InvariantCulture);
        IdsList = idsList;
    }
}