-- The service's role can add audit rows and read them, never change or remove them.
GRANT SELECT, INSERT, UPDATE, DELETE ON diagnoses, consents TO clinic_app;
GRANT SELECT, INSERT ON phi_audit TO clinic_app;
GRANT USAGE ON SEQUENCE diagnoses_hilo TO clinic_app;
