#!/usr/bin/env python3
import requests
import os
import time
import json
import subprocess

BASE_URL = "http://localhost:3000"
TEST_DOCS_DIR = r"D:\project in this pc\PFE\GED-Search-Engine\test-docs"
CATEGORY = "Invoice"

def login():
    session = requests.Session()
    resp = session.post(f"{BASE_URL}/api/auth/login", json={
        "username": "admin",
        "password": "Admin@1234"
    })
    if resp.status_code != 200:
        print(f"Login failed: {resp.status_code} {resp.text}")
        return None
    print(f"=== Login Successful ===")
    return session

def upload_documents(session):
    docs_dir = TEST_DOCS_DIR
    files = [f for f in os.listdir(docs_dir) if os.path.isfile(os.path.join(docs_dir, f)) and f.endswith('.pdf')]
    
    uploaded_ids = []
    print(f"\n=== Uploading {len(files)} Documents ===")
    for filename in sorted(files):
        filepath = os.path.join(docs_dir, filename)
        
        import mimetypes
        content_type, _ = mimetypes.guess_type(filepath)
        if content_type is None:
            content_type = 'application/pdf'
        
        with open(filepath, 'rb') as f:
            file_content = f.read()
        
        from io import BytesIO
        files_data = {'file': (filename, BytesIO(file_content), content_type)}
        data = {'category': CATEGORY}
        
        try:
            resp = session.post(f"{BASE_URL}/api/documents/upload", files=files_data, data=data)
            if resp.status_code in (200, 201):
                doc = resp.json()
                print(f"Uploaded: {filename} -> {doc.get('id')} (Status: {doc.get('status')})")
                uploaded_ids.append(doc.get('id'))
            else:
                print(f"Failed: {filename} - {resp.text[:100]}")
        except Exception as e:
            print(f"Error: {filename} - {e}")
    
    print(f"\n=== Upload Complete: {len(uploaded_ids)} documents ===")
    return uploaded_ids

def wait_and_check_status(delay_minutes=5):
    print(f"\n=== Waiting {delay_minutes} minutes for processing ===")
    for i in range(delay_minutes * 60, 0, -60):
        print(f"  Remaining: {i}s...", end='\r')
        time.sleep(60)
    print()
    
    print("\n=== Final Status ===")
    
    # Database
    result = subprocess.run([
        "docker", "run", "--rm", "--network", "ged-search-engine_ged-network",
        "mcr.microsoft.com/mssql/server:2022-latest",
        "/opt/mssql-tools18/bin/sqlcmd", "-S", "sqlserver", "-U", "sa",
        "-P", "GedPass_2024!", "-d", "ged_db",
        "-Q", "SELECT Status, COUNT(*) as Count FROM documents GROUP BY Status", "-C"
    ], capture_output=True, text=True, encoding='utf-8')
    print(result.stdout)
    
    # OpenSearch
    result = subprocess.run([
        "curl", "-s", "http://localhost:9200/ged-documents/_search?size=100",
        "-u", "admin:GedOpensearch2024!"
    ], capture_output=True, text=True, encoding='utf-8')
    data = json.loads(result.stdout)
    pending = [h for h in data['hits']['hits'] if h['_source'].get('status') == 'Pending']
    indexed = [h for h in data['hits']['hits'] if h['_source'].get('status') == 'Indexed']
    print(f"OpenSearch: Total={data['hits']['total']['value']}, Pending={len(pending)}, Indexed={len(indexed)}")

if __name__ == "__main__":
    session = login()
    if session:
        ids = upload_documents(session)
        if ids:
            wait_and_check_status(10)  # 10 minutes
        else:
            print("No documents uploaded")