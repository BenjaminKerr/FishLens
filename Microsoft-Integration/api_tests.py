# Created by: Kaharra
# Purpose: This code will allow the user to login to their microsfot account granting our app (through Azure Entra) 
# access to read objects of their account using Microsfot Graph
# Notes: There is another step I oringally skipped which is a redirect URL link, but this is only needed through integration once we know 
# where we want our login set up (Whether before they get into our site as the login critera or after) we will be able to redirect user back to our
# site/page. 

import msal

CLIENT_ID = "aed2d2f8-02e0-413c-a365-cdf377f5c329"
AUTHORITY = "https://login.microsoftonline.com/common"
SCOPES = ["User.Read"]

app = msal.PublicClientApplication(client_id=CLIENT_ID, authority=AUTHORITY)

flow = app.initiate_device_flow(scopes=SCOPES)
print(flow["message"]) 

result = app.acquire_token_by_device_flow(flow)

if "access_token" in result:
    print("Login worked token received")
else:
    print("Login failed")
    print(result.get("error_description"))
