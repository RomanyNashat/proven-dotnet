"""api_handoff.py on the visits API's real documents (fixture/, kept what ASP.NET Core generates by
HandoffFixtureTests) and on hand-written ones for what that API doesn't have: 3.0 spellings, headers,
removed fields, a type change, a cycle."""
import copy
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parents[1] / ".claude" / "skills" / "api-contract-evolution" / "scripts" / "api_handoff.py"
FIXTURE = HERE / "fixture"


def run(*args, env=None):
    return subprocess.run([sys.executable, str(SCRIPT), *map(str, args)], capture_output=True, text=True, encoding="utf-8", env=env)


def handoff(old, new):
    return json.loads(run(old, new, "--json").stdout)


def by_endpoint(h):
    return {e["endpoint"]: e for e in h["endpoints"]}


# An API in the 3.0 spelling, with what the visits API lacks: headers, path-level parameters, allOf, examples.
BASE = {
    "openapi": "3.0.3",
    "info": {"title": "Appointments", "version": "1.4.0"},
    "paths": {
        "/appointments/{id}": {
            "parameters": [{"$ref": "#/components/parameters/Id"}],
            "get": {
                "parameters": [{"name": "X-API-Version", "in": "header", "required": True, "schema": {"type": "string"}}],
                "responses": {
                    "200": {"description": "The appointment",
                            "headers": {"Deprecation": {"schema": {"type": "string"}}},
                            "content": {"application/json": {"schema": {"$ref": "#/components/schemas/Appointment"}}}},
                    "404": {"description": "No appointment with this id, or not yours",
                            "content": {"application/problem+json": {"schema": {"type": "object"}}}},
                },
            },
        },
        "/appointments": {
            "post": {
                "requestBody": {"required": True, "content": {"application/json": {"schema": {"$ref": "#/components/schemas/NewAppointment"}}}},
                "responses": {"201": {"description": "Created"}},
            },
            "get": {
                "parameters": [{"name": "day", "in": "query", "schema": {"type": "string", "format": "date"}}],
                "responses": {"200": {"description": "OK", "content": {"application/json": {"schema": {"type": "array", "items": {"$ref": "#/components/schemas/Appointment"}}}}}},
            },
        },
        "/clinics": {"get": {"responses": {"200": {"description": "OK"}}}},
    },
    "components": {
        "parameters": {"Id": {"name": "id", "in": "path", "required": True, "schema": {"type": "integer"}}},
        "schemas": {
            "Base": {"type": "object", "required": ["id"], "properties": {"id": {"type": "integer"}}},
            "Appointment": {"allOf": [{"$ref": "#/components/schemas/Base"}, {
                "type": "object", "required": ["clinic", "startsAt", "kind"],
                "properties": {
                    "clinic": {"type": "string", "example": "عيادة الأسنان"},
                    "startsAt": {"type": "string", "format": "date-time"},
                    "kind": {"type": "string", "enum": ["InPerson", "Video"]},
                    "doctorNote": {"type": "string"},
                    "price": {"type": "number"},
                    "referral": {"$ref": "#/components/schemas/Referral"},
                }}]},
            "Referral": {"type": "object", "properties": {"from": {"type": "string"}, "next": {"$ref": "#/components/schemas/Referral"}}},
            "NewAppointment": {"type": "object", "required": ["slotId"], "properties": {
                "slotId": {"type": "integer"}, "kind": {"type": "string", "enum": ["InPerson", "Video", "Phone"]},
                "note": {"type": "string", "nullable": True}}},
        },
    },
}


class Stories(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="handoff "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def write(self, name, doc):
        path = self.tmp / name
        path.write_text(json.dumps(doc), encoding="utf-8")
        return path

    def test_story_the_new_status_is_flagged_for_the_mobile_team_with_what_to_do(self):
        # Given the two releases' documents as ASP.NET Core generates them
        h = handoff(FIXTURE / "v1.json", FIXTURE / "v2.json")
        eps = by_endpoint(h)
        # Then the visit read breaks installed apps, because of Cancelled, and says how to ship it
        get = eps["GET /visits/{id}"]
        self.assertEqual("breaks", get["impact"])
        cancelled = get["changes"][0]
        self.assertEqual(("status", "enum adds Cancelled", "response 200"), (cancelled["field"], cancelled["what"], cancelled["where"]))
        self.assertIn("only to app versions that know it", cancelled["why"])
        # And the room is safe, the cancel endpoint is new, with its errors as ProblemDetails
        self.assertEqual("safe", next(c for c in get["changes"] if c["field"] == "room")["impact"])
        delete = eps["DELETE /visits/{id}"]
        self.assertEqual(("new", "safe", "204"), (delete["status"], delete["impact"], delete["ok"]))
        self.assertEqual(["404", "409"], [e["status"] for e in delete["errors"]])
        self.assertEqual("ProblemDetails (type, title, status, detail)", delete["errors"][0]["body"])
        # And the visit list is first: what breaks comes before what's safe
        self.assertEqual("GET /visits/{id}", h["endpoints"][0]["endpoint"])

    def test_story_a_new_required_request_field_breaks_every_booking_from_an_installed_app(self):
        post = by_endpoint(handoff(FIXTURE / "v1.json", FIXTURE / "v2-required.json"))["POST /visits"]
        self.assertEqual([("reason", "new required field", "breaks")], [(c["field"], c["what"], c["impact"]) for c in post["changes"]])
        self.assertIn("rejected (400)", post["changes"][0]["why"])

    def test_story_the_markdown_the_mobile_team_reads(self):
        out = run(FIXTURE / "v1.json", FIXTURE / "v2.json", "--headers", "App-Version, x-language").stdout
        self.assertTrue(out.startswith("# API changes for the mobile team: v1.json → v2.json\n\n2 endpoints changed; 1 breaks app versions already installed.\n"))
        self.assertIn("Every call sends `App-Version`, `x-language`.", out)
        self.assertIn("| `status` enum adds Cancelled | response 200 | breaks installed apps |", out)
        self.assertIn('"status": "Booked"', out)
        self.assertIn("- 404: ProblemDetails (type, title, status, detail)\n", out)  # no "Not Found" repeated

    def test_story_a_release_that_removes_retypes_and_tightens(self):
        new = copy.deepcopy(BASE)
        appt = new["components"]["schemas"]["Appointment"]["allOf"][1]
        del appt["properties"]["clinic"]                                 # required: an app can't read it
        del appt["properties"]["doctorNote"]                             # optional: a blank
        appt["properties"]["price"] = {"type": "string"}                 # retyped
        appt["properties"]["startsAt"]["nullable"] = True                # now nullable
        appt["required"] = ["startsAt"]                                  # kind now optional
        req = new["components"]["schemas"]["NewAppointment"]
        req["properties"]["kind"]["enum"] = ["InPerson", "Video"]        # Phone dropped from a request
        req["properties"]["note"]["nullable"] = False                    # null no longer accepted
        new["paths"]["/appointments"]["get"]["parameters"].append({"name": "clinicId", "in": "query", "required": True, "schema": {"type": "integer"}})
        del new["paths"]["/clinics"]
        new["paths"]["/appointments/{id}"]["get"]["deprecated"] = True
        eps = by_endpoint(handoff(self.write("old.json", BASE), self.write("new.json", new)))

        got = {(c["where"], c["field"], c["what"]): c["impact"] for c in eps["GET /appointments/{id}"]["changes"]}
        self.assertEqual("breaks", got[("response 200", "clinic", "removed")])
        self.assertEqual("risky", got[("response 200", "doctorNote", "removed")])
        self.assertEqual("breaks", got[("response 200", "price", "type number → string")])
        self.assertEqual("breaks", got[("response 200", "startsAt", "can now be null")])
        self.assertEqual("risky", got[("response 200", "kind", "now optional")])
        self.assertEqual("note", got[("endpoint", "", "deprecated")])
        # The list returns the same Appointment, as array items
        self.assertEqual("breaks", {c["field"]: c["impact"] for c in eps["GET /appointments"]["changes"]}["[].clinic"])
        self.assertEqual("breaks", {c["field"]: c["impact"] for c in eps["GET /appointments"]["changes"]}["clinicId"])
        post = {(c["field"], c["what"]): c["impact"] for c in eps["POST /appointments"]["changes"]}
        self.assertEqual("breaks", post[("kind", "enum drops Phone")])
        self.assertEqual("breaks", post[("note", "can't be null any more")])
        self.assertEqual(("removed", "breaks"), (eps["GET /clinics"]["status"], eps["GET /clinics"]["impact"]))

    def test_story_what_is_safe_stays_safe(self):
        new = copy.deepcopy(BASE)
        appt = new["components"]["schemas"]["Appointment"]["allOf"][1]
        appt["properties"]["room"] = {"type": "string"}                                     # a new optional field
        req = new["components"]["schemas"]["NewAppointment"]
        req["properties"]["kind"]["enum"].append("HomeVisit")                             # a request accepts more
        req["properties"]["contact"] = {"type": "object", "required": ["phone"], "properties": {"phone": {"type": "string"}}}
        new["paths"]["/appointments"]["get"]["parameters"].append({"name": "page", "in": "query", "schema": {"type": "integer"}})
        h = handoff(self.write("old.json", BASE), self.write("new.json", new))
        impacts = {c["impact"] for e in h["endpoints"] for c in e["changes"]}
        self.assertEqual({"safe"}, impacts)  # `contact.phone` is required only inside a new optional object

    def test_the_details_the_team_needs_headers_examples_errors(self):
        new = copy.deepcopy(BASE)
        new["components"]["schemas"]["Appointment"]["allOf"][1]["properties"]["room"] = {"type": "string"}
        get = by_endpoint(handoff(self.write("old.json", BASE), self.write("new.json", new)))["GET /appointments/{id}"]
        self.assertEqual([{"name": "X-API-Version", "required": True}], get["headers"])
        self.assertEqual(["Deprecation"], get["response_headers"])
        self.assertEqual("عيادة الأسنان", get["response"]["clinic"])           # the document's own example wins
        self.assertEqual("2026-01-31T09:30:00Z", get["response"]["startsAt"])
        self.assertEqual({"from": "string", "next": None}, {k: v if not isinstance(v, dict) else None for k, v in get["response"]["referral"].items()})
        self.assertEqual("No appointment with this id, or not yours", get["errors"][0]["description"])

    def test_nothing_changed_and_fail_on_break(self):
        same = self.write("old.json", BASE)
        out = run(same, self.write("new.json", BASE))
        self.assertIn("Nothing an app calls has changed.", out.stdout)
        self.assertEqual(1, run(FIXTURE / "v1.json", FIXTURE / "v2.json", "--fail-on-break").returncode)
        self.assertEqual(0, run(FIXTURE / "v1.json", FIXTURE / "v2.json").returncode)

    def test_a_yaml_document_is_refused_with_what_to_do(self):
        path = self.tmp / "openapi.yaml"
        path.write_text("openapi: 3.0.3\ninfo:\n  title: x\n", encoding="utf-8")
        out = run(path, FIXTURE / "v1.json")
        self.assertEqual(2, out.returncode)
        self.assertIn("can't read it as JSON", out.stdout)


class Production(unittest.TestCase):
    def test_documents_saved_by_windows_powershell_with_arabic_on_a_cp1252_console(self):
        tmp = Path(tempfile.mkdtemp(prefix="handoff win "))
        try:
            new = copy.deepcopy(BASE)
            del new["paths"]["/clinics"]
            (tmp / "قديم.json").write_bytes(json.dumps(BASE, ensure_ascii=False, indent=2).replace("\n", "\r\n").encode("utf-16"))  # PowerShell 5.1's `>`
            (tmp / "جديد.json").write_bytes("﻿".encode() + json.dumps(new, ensure_ascii=False).encode("utf-8"))
            env = dict(os.environ, PYTHONIOENCODING="cp1252")
            out = subprocess.run([sys.executable, str(SCRIPT), str(tmp / "قديم.json"), str(tmp / "جديد.json")], capture_output=True, env=env)
            text = out.stdout.decode("utf-8")
            self.assertEqual(0, out.returncode, out.stderr.decode("utf-8", "replace"))
            self.assertIn("## GET /clinics (removed, breaks installed apps)", text)
            self.assertIn("# API changes for the mobile team: قديم.json → جديد.json", text)  # the same title and version
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
