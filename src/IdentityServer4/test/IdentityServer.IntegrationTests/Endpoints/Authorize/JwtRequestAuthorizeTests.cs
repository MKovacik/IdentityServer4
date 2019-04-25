// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using System.Net;
using System.Collections.Generic;
using IdentityServer4.Models;
using System.Security.Claims;
using IdentityServer4.IntegrationTests.Common;
using IdentityServer4.Test;
using System.Net.Http;
using IdentityModel;
using System;
using System.Security.Cryptography.X509Certificates;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace IdentityServer4.IntegrationTests.Endpoints.Authorize
{
    public class JwtRequestAuthorizeTests
    {
        private const string Category = "Authorize endpoint with JWT requests";

        private IdentityServerPipeline _mockPipeline = new IdentityServerPipeline();

        private Client _client;

        public JwtRequestAuthorizeTests()
        {
            _mockPipeline.Clients.AddRange(new Client[] {
                _client = new Client
                {
                    ClientName = "Client with Base64 encoded X509 Certificate",
                    ClientId = "certificate_base64_valid",
                    Enabled = true,

                    RedirectUris = { "https://client/callback" },

                    ClientSecrets =
                    {
                        new Secret
                        {
                            Type = IdentityServerConstants.SecretTypes.X509CertificateBase64,
                            Value = Convert.ToBase64String(TestCert.Load().Export(X509ContentType.Cert))
                        }
                    },

                    AllowedGrantTypes = GrantTypes.Implicit,

                    AllowedScopes = new List<string>
                    {
                        "openid", "profile", "api1", "api2"
                    }
                },
            });

            _mockPipeline.Users.Add(new TestUser
            {
                SubjectId = "bob",
                Username = "bob",
                Claims = new Claim[]
                {
                    new Claim("name", "Bob Loblaw"),
                    new Claim("email", "bob@loblaw.com"),
                    new Claim("role", "Attorney")
                }
            });

            _mockPipeline.IdentityScopes.AddRange(new IdentityResource[] {
                new IdentityResources.OpenId(),
                new IdentityResources.Profile(),
                new IdentityResources.Email()
            });
            _mockPipeline.ApiScopes.AddRange(new ApiResource[] {
                new ApiResource
                {
                    Name = "api",
                    Scopes =
                    {
                        new Scope
                        {
                            Name = "api1"
                        },
                        new Scope
                        {
                            Name = "api2"
                        }
                    }
                }
            });

            _mockPipeline.Initialize();
        }

        string CreateRequestJwt(Claim[] claims)
        {
            var creds = new X509SigningCredentials(TestCert.Load());
            var handler = new JwtSecurityTokenHandler();

            var token = handler.CreateJwtSecurityToken(
                issuer: _client.ClientId, 
                audience: IdentityServerPipeline.BaseUrl, 
                signingCredentials:creds, 
                subject: Identity.Create("pwd", claims));

            return handler.WriteToken(token);
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task authorize_should_accept_JWT_request_object_parameters()
        {
            var requestJwt = CreateRequestJwt(new[] {
                new Claim("response_type", "id_token"),
                new Claim("scope", "openid profile"),
                new Claim("state", "123state"),
                new Claim("nonce", "123nonce"),
                new Claim("redirect_uri", "https://client/callback"),
                new Claim("acr_values", "acr_1 acr_2 tenant:tenant_value idp:idp_value"),
                new Claim("login_hint", "login_hint_value"),
                new Claim("display", "popup"),
                new Claim("ui_locales", "ui_locale_value"),
                new Claim("foo", "123foo"),
            });

            var url = _mockPipeline.CreateAuthorizeUrl(
                clientId: _client.ClientId,
                responseType: "id_token",
                extra: new
                {
                    request = requestJwt
                });
            var response = await _mockPipeline.BrowserClient.GetAsync(url);

            _mockPipeline.LoginRequest.Should().NotBeNull();
            _mockPipeline.LoginRequest.ClientId.Should().Be(_client.ClientId);
            _mockPipeline.LoginRequest.DisplayMode.Should().Be("popup");
            _mockPipeline.LoginRequest.UiLocales.Should().Be("ui_locale_value");
            _mockPipeline.LoginRequest.IdP.Should().Be("idp_value");
            _mockPipeline.LoginRequest.Tenant.Should().Be("tenant_value");
            _mockPipeline.LoginRequest.LoginHint.Should().Be("login_hint_value");
            _mockPipeline.LoginRequest.AcrValues.Should().BeEquivalentTo(new string[] { "acr_2", "acr_1" });
            _mockPipeline.LoginRequest.Parameters.AllKeys.Should().Contain("foo");
            _mockPipeline.LoginRequest.Parameters["foo"].Should().Be("123foo");
        }
    }
}
