# -*- coding: utf-8 -*-
"""Работа с таблицей баланса в Google Sheets через сервисный аккаунт аналитики."""
import io

from google.oauth2.service_account import Credentials
from googleapiclient.discovery import build

KEY = r"C:\Users\User\.claude\secrets\Gsheet.json"
SHEET = "1WMXfmePxXbQzRl0IM-XWX53ZYOO0Ml2OLlm2OGnCSPg"

_api = None

# Таблица пускает шестьдесят записей в минуту на пользователя, а полная перезаливка книги
# их втрое больше: очистить, вписать, причесать — и так на каждую из тридцати вкладок.
# Оттого заливка срывалась на середине, оставляя часть листов пустыми. Ждём и повторяем.
def patiently(request, tries=6, wait=20):
    """Выполнить запрос, пережидая отказ по частоте."""
    import time

    from googleapiclient.errors import HttpError

    for left in range(tries, 0, -1):
        try:
            return request.execute()
        except HttpError as no:
            if getattr(no, "resp", None) is None or no.resp.status != 429 or left == 1:
                raise
            time.sleep(wait)

    return None


def api():
    global _api
    if _api is None:
        creds = Credentials.from_service_account_file(
            KEY, scopes=["https://www.googleapis.com/auth/spreadsheets"])
        _api = build("sheets", "v4", credentials=creds, cache_discovery=False)
    return _api


def tabs():
    """Что за вкладки сейчас в книге: имя -> id."""
    meta = api().spreadsheets().get(spreadsheetId=SHEET).execute()
    return {s["properties"]["title"]: s["properties"]["sheetId"] for s in meta["sheets"]}


def ensure(names):
    """Завести недостающие вкладки, вернуть имя -> id по всем."""
    have = tabs()
    want = [n for n in names if n not in have]

    if want:
        api().spreadsheets().batchUpdate(spreadsheetId=SHEET, body={
            "requests": [{"addSheet": {"properties": {"title": n}}} for n in want]
        }).execute()
        have = tabs()

    return have


def put(name, head, rows, widths=None):
    """Вписать шапку и строки во вкладку, затерев прежнее."""
    ids = ensure([name])
    sheet_id = ids[name]

    patiently(api().spreadsheets().values().clear(
        spreadsheetId=SHEET, range="'%s'" % name, body={}))

    patiently(api().spreadsheets().values().update(
        spreadsheetId=SHEET, range="'%s'!A1" % name,
        valueInputOption="RAW",
        body={"values": [head] + rows}))

    asks = [
        # Шапка жирная и закреплена — иначе на трёхстах строках теряешься.
        {"repeatCell": {
            "range": {"sheetId": sheet_id, "startRowIndex": 0, "endRowIndex": 1},
            "cell": {"userEnteredFormat": {
                "textFormat": {"bold": True},
                "backgroundColor": {"red": 0.93, "green": 0.93, "blue": 0.93}}},
            "fields": "userEnteredFormat(textFormat,backgroundColor)"}},
        {"updateSheetProperties": {
            "properties": {"sheetId": sheet_id,
                           "gridProperties": {"frozenRowCount": 1}},
            "fields": "gridProperties.frozenRowCount"}},
        {"autoResizeDimensions": {
            "dimensions": {"sheetId": sheet_id, "dimension": "COLUMNS",
                           "startIndex": 0, "endIndex": len(head)}}},
    ]

    patiently(api().spreadsheets().batchUpdate(
        spreadsheetId=SHEET, body={"requests": asks}))

    return len(rows)


def drop(name):
    """Убрать вкладку, если она есть."""
    have = tabs()
    if name not in have:
        return False

    api().spreadsheets().batchUpdate(spreadsheetId=SHEET, body={
        "requests": [{"deleteSheet": {"sheetId": have[name]}}]}).execute()
    return True


def order(names):
    """Расставить вкладки в заданном порядке."""
    have = tabs()
    asks = []
    for i, n in enumerate(names):
        if n in have:
            asks.append({"updateSheetProperties": {
                "properties": {"sheetId": have[n], "index": i},
                "fields": "index"}})
    if asks:
        api().spreadsheets().batchUpdate(spreadsheetId=SHEET, body={"requests": asks}).execute()
