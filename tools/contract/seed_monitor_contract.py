#!/usr/bin/env python3
"""INT-009: prepares a running Licensing Platform (Development, Seed:DemoData=true) for Monitor Cloud's live
contract suite and prints the LICENSING_CONTRACT_* variables it needs.

Uses only the public APIs, as the demo tenant's administrator. Never run it against production: it creates a
product, four plans, a customer, a subscription and licenses. Every value it prints is test-only demo data.

    python3 tools/contract/seed_monitor_contract.py http://127.0.0.1:5200 > contract.env
"""
import json
import sys
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5200").rstrip("/")
ADMIN, PASSWORD = "admin@nour.test", "Demo@12345"                # DbInitializer.DemoAccounts (demo data only)
CLIENT_ID, CLIENT_SECRET = "lc_demo_nour", "lcs_demo_nour_secret_change_me"
PRODUCT_CODE = "000001"
PLANS = ["STARTER", "PROFESSIONAL", "BUSINESS", "ENTERPRISE"]
FEATURES = ["monitoring", "alerts", "reports"]


def call(method, path, body=None, token=None, expect=(200, 201, 204)):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(BASE + path, data=data, method=method, headers={"Content-Type": "application/json"})
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(request) as response:
            text = response.read().decode()
            status = response.status
    except urllib.error.HTTPError as error:
        text, status = error.read().decode(), error.code
    if status not in expect:
        raise SystemExit(f"{method} {path} -> {status} {text[:300]}")
    return json.loads(text) if text else None


def main():
    token = call("POST", "/api/v1/auth/login", {"email": ADMIN, "password": PASSWORD})["accessToken"]
    suffix = uuid.uuid4().hex[:6].upper()

    products = call("GET", "/api/v1/products?pageSize=200", token=token)
    existing = [p for p in products.get("items", products) if p.get("code") == PRODUCT_CODE]
    product_id = existing[0]["id"] if existing else call("POST", "/api/v1/products", {"code": PRODUCT_CODE, "name": "Monitor Agent"}, token)["id"]

    plan_ids = {}
    for code in PLANS:
        plan = call("POST", "/api/v1/plans", {
            "productId": product_id, "code": code, "name": code.title(), "price": 100, "currency": "EGP", "durationDays": 365,
            "maxActivations": None, "heartbeatIntervalHours": 24, "offlineGraceDays": 7, "features": FEATURES,
        }, token, expect=(201, 409))
        if plan and "id" in plan:
            call("POST", f"/api/v1/plans/{plan['id']}/publish", token=token)
            plan_ids[code] = plan["id"]
    for plan in call("GET", "/api/v1/plans?pageSize=200", token=token)["items"]:
        if plan["productId"] == product_id and plan["code"] in PLANS:
            plan_ids.setdefault(plan["code"], plan["id"])
    short = call("POST", "/api/v1/plans", {
        "productId": product_id, "code": f"SHORT{suffix}", "name": "Short", "price": 0, "currency": "EGP", "durationDays": 1,
        "maxActivations": None, "heartbeatIntervalHours": 24, "offlineGraceDays": 7, "features": FEATURES,
    }, token)
    call("POST", f"/api/v1/plans/{short['id']}/publish", token=token)

    def customer(name):
        return call("POST", "/api/v1/customers", {"name": name}, token)["customer"]["id"]

    def subscription(customer_id, plan_id, start=None):
        body = {"customerId": customer_id, "planId": plan_id}
        if start:
            body["startDate"] = start
        return call("POST", "/api/v1/subscriptions", body, token)["subscription"]["id"]

    def license(subscription_id):
        issued = call("POST", "/api/v1/licenses", {"subscriptionId": subscription_id}, token)
        return issued["license"]["id"], issued["productKey"]

    # The contract customer has exactly one subscription and one license (entitlements test).
    main_customer = customer(f"Contract Customer {suffix}")
    _, active_key = license(subscription(main_customer, plan_ids["ENTERPRISE"]))

    others = customer(f"Contract Extras {suffix}")
    limited_plan = call("POST", "/api/v1/plans", {
        "productId": product_id, "code": f"LIMIT{suffix}", "name": "Two devices", "price": 0, "currency": "EGP", "durationDays": 365,
        "maxActivations": 2, "heartbeatIntervalHours": 24, "offlineGraceDays": 7, "features": FEATURES,
    }, token)
    call("POST", f"/api/v1/plans/{limited_plan['id']}/publish", token=token)
    _, limited_key = license(subscription(others, limited_plan["id"]))
    suspended_id, suspended_key = license(subscription(others, plan_ids["PROFESSIONAL"]))
    call("POST", f"/api/v1/licenses/{suspended_id}/suspend", {"reason": "contract"}, token)
    past = (datetime.now(timezone.utc) - timedelta(days=3)).isoformat()
    _, expired_key = license(subscription(others, short["id"], past))

    print(f"LICENSING_CONTRACT_URL={BASE}")
    print(f"LICENSING_CONTRACT_CLIENT_ID={CLIENT_ID}")
    print(f"LICENSING_CONTRACT_CLIENT_SECRET={CLIENT_SECRET}")
    print(f"LICENSING_CONTRACT_PRODUCT_CODE={PRODUCT_CODE}")
    print(f"LICENSING_CONTRACT_PRODUCT_KEY={active_key}")
    print(f"LICENSING_CONTRACT_CUSTOMER_ID={main_customer}")
    print(f"LICENSING_CONTRACT_SUSPENDED_KEY={suspended_key}")
    print(f"LICENSING_CONTRACT_EXPIRED_KEY={expired_key}")
    print(f"LICENSING_CONTRACT_LIMITED_KEY={limited_key}")


if __name__ == "__main__":
    main()
