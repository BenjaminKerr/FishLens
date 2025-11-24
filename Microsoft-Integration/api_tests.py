# Created by: Kaharra
# Purpose: This code will allow the user to login to their microsfot account granting our app (through Azure Entra) 
# access to read objects of their account using Microsfot Graph
# Notes: There is another step I oringally skipped which is a redirect URL link, but this is only needed through integration once we know 
# where we want our login set up (Whether before they get into our site as the login critera or after) we will be able to redirect user back to our
# site/page. This code will get the user to login and request permissions from the user upon login, and will pull all Excell spreadsheets in one drive
# 

import msal
import requests
import webbrowser

CLIENT_ID = "aed2d2f8-02e0-413c-a365-cdf377f5c329"
AUTHORITY = "https://login.microsoftonline.com/common"
SCOPES = ["User.Read", "Files.ReadWrite"]

app = msal.PublicClientApplication(client_id=CLIENT_ID, authority=AUTHORITY)

flow = app.initiate_device_flow(scopes=SCOPES)

webbrowser.open(flow["verification_uri"])
print("\n--- Microsoft Login ---")
print("The login window will now open for you\n")
print("Where asked enter this code", flow["user_code"])

result = app.acquire_token_by_device_flow(flow)

# Function: select_excel_sheet
# Purpose: This function will pull available excel spreadsheets from the microsoft users main one drive and promnpt user to select one
# 
def select_excel_sheet(access_token):
    headers = {"Authorization": f"Bearer {access_token}"}

    print("\nChecking your OneDrive...")
    drive_check = requests.get(
        "https://graph.microsoft.com/v1.0/me/drive",
        headers=headers
    )

    if drive_check.status_code != 200:
        print("Can’t open OneDrive")
        print("might need to open OneDrive once or check your account permissions")
        return None

    print("Looking for Excel files in your OneDrive root folder...")

    resp = requests.get(
        "https://graph.microsoft.com/v1.0/me/drive/root/children?$top=200",
        headers=headers
    )
    resp.raise_for_status()

    items = resp.json().get("value", [])
    excel_exts = (".xlsx", ".xlsm", ".xls")

    excel_items = [
        it for it in items
        if it.get("name", "").lower().endswith(excel_exts)
        and "folder" not in it
    ]

    if not excel_items:
        print("\nNo Excel files found in your OneDrive root folder")
        print("Try uploading one to the main OneDrive area and run again")
        return None

    print("\nPick a spreadsheet:\n")
    for i, it in enumerate(excel_items, start=1):
        print(f"{i}) {it.get('name')}")

    while True:
        choice = input("\nType the number of the file you want: ").strip()
        if choice.isdigit():
            idx = int(choice)
            if 1 <= idx <= len(excel_items):
                return excel_items[idx - 1]
        print("That wasn’t one of the options, try again.")

# Function: open_selected_sheet
# Purpose: This function will open the spreadsheet in the users browser they had selected
# 
def open_selected_sheet(selected_item):
    if not selected_item:
        return

    url = selected_item.get("webUrl")
    if not url:
        print("Couldn’t find the link to open this file")
        return

    print(f"\nOpening '{selected_item.get('name')}' in your browser")
    webbrowser.open(url)


# Function: add_row_to_selected_sheet
# Purpose: This function will add a single row of data based on user input, it also pulls current column names so user knows what to input
# 
def add_row_to_selected_sheet(access_token, selected_item):
    headers = {
        "Authorization": f"Bearer {access_token}",
        "Content-Type": "application/json"
    }

    file_id = selected_item["id"]

  
    ws_resp = requests.get(
        f"https://graph.microsoft.com/v1.0/me/drive/items/{file_id}/workbook/worksheets",
        headers=headers
    )
    ws_resp.raise_for_status()
    worksheets = ws_resp.json().get("value", [])
    if not worksheets:
        print("No worksheets found in that spreadsheet")
        return

    sheet_id = worksheets[0]["id"]
    sheet_name = worksheets[0]["name"]
    print(f"\nUsing sheet: {sheet_name}")

    
    used_resp = requests.get(
        f"https://graph.microsoft.com/v1.0/me/drive/items/{file_id}/workbook/worksheets/{sheet_id}/usedRange(valuesOnly=true)",
        headers=headers
    )
    used_resp.raise_for_status()
    values = used_resp.json().get("values", [])

    if not values or len(values) < 1:
        print("Sheet is empty, no column names written")
        return

    
    column_names = values[0]
    print("\nColumns found:")
    for c in column_names:
        print(c)

   
    new_row = []
    print("\nEnter the value for each column:")
    for c in column_names:
        val = input(f"{c}: ")
        new_row.append(val)

    
    next_row_number = len(values) + 1  

    def col_letter(n):
        s = ""
        while n > 0:
            n, r = divmod(n - 1, 26)
            s = chr(65 + r) + s
        return s

    last_col_letter = col_letter(len(column_names))
    address = f"A{next_row_number}:{last_col_letter}{next_row_number}"

    patch_resp = requests.patch(
        f"https://graph.microsoft.com/v1.0/me/drive/items/{file_id}/workbook/worksheets/{sheet_id}/range(address='{address}')",
        headers=headers,
        json={"values": [new_row]}
    )
    patch_resp.raise_for_status()

    print("\nNew row added in row", next_row_number)



if "access_token" in result:
    print("\nLogin worked token recieved")
    selected = select_excel_sheet(result["access_token"])

    if selected:
        print("\nYou picked:")
        print(f"• {selected['name']}")
        print("(Will use file ID later to add data)")
        open_selected_sheet(selected)
        add_row_to_selected_sheet(result["access_token"], selected)
else:
    print("\nLogin failed")
    print(result.get("error_description"))