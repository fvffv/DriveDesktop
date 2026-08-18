using System;
using System.Collections.Generic;
using LiveMarkdown.Avalonia;

namespace drive_desktop.Models;


public class UserInfoEdit
{
    public string UserNick { get; set; }
      
}
public class UserPasswordEdit
{
    public string OldPassword { get; set; }
    public string NewPassword { get; set; }
}

public class CloudInfo
{
    public string Name  { get; set; }
    public long MaxFileSize { get; set; }
    public long FileChunkSizeBytes { get; set; }
        
}


public class AiChat
{
    
}

public class AiChatAiMsg : AiChat
{
    public ObservableStringBuilder Content { get; set; }
    public string Time
    {
        get => field =  DateTime.Now.ToString("HH:mm:ss");
        set ;
    }
    
}
public class AiChatUserMsg : AiChat
{
  
    public string Content { get; set; }
    public string Time
    {
        get => field =  DateTime.Now.ToString("HH:mm:ss");
        set;
    }
}

public class AiChatRequest
{
    public string UserInput { get; set; }

    public List<ChatMessage> History { get; set; }

    public ClientContext Context { get; set; }
}
public class ChatMessage
{
    public string Role { get; set; }

    public string Content { get; set; }
}

public class ClientContext
{
    public string CurrentFolderId { get; set; }

    public string CurrentPath { get; set; }

    public string CurrentDomain { get; set; }

    public string Client { get; set; }

    public string BackEnd { get; set; }
}